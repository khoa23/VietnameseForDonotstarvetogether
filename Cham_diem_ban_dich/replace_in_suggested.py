"""
replace_in_suggested.py
-----------------------
Dùng từ điển trong strings_out_header.csv để REPLACE nội dung
cột SuggestedTranslation trong database với word boundary (\b)
để tránh thay nhầm trong từ.

Ví dụ: từ "icker" sẽ KHÔNG thay trong "Wickerbottom" vì "icker"
không đứng đầu/cuối từ.

Chiến lược:
  1. Fetch toàn bộ SuggestedTranslation về Python
  2. Dùng re.sub(r'\b{từ}\b', ...) để thay đúng ranh giới từ
  3. UPDATE lại DB chỉ những bản ghi thực sự thay đổi

Cách dùng:
  python replace_in_suggested.py                 # Chạy thật (6 luồng)
  python replace_in_suggested.py --threads 4     # Chạy với N luồng
  python replace_in_suggested.py --dry-run       # Xem trước, không ghi DB
  python replace_in_suggested.py --info          # Chỉ thống kê
"""

import csv
import json
import logging
import re
import sys
import threading
import pyodbc
from concurrent.futures import ThreadPoolExecutor, as_completed
from typing import Dict, List, Optional, Tuple

logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)

CSV_FILE        = 'strings_out_header.csv'
CONFIG_FILE     = 'config.json'
DEFAULT_WORKERS = 6

# Lock để in log an toàn từ nhiều luồng
_print_lock = threading.Lock()


# ---------------------------------------------------------
# 1. Đọc từ điển CSV
# ---------------------------------------------------------

def load_dictionary(csv_path: str) -> List[Tuple[str, str, re.Pattern]]:
    """
    Đọc file CSV (cột 1 = Tiếng Anh, cột 2 = Tiếng Việt).
    Trả về list (en_text, vi_text, compiled_regex).
    - Sắp xếp từ DÀI trước để tránh thay nhầm (VD: 'Crock Pot' trước 'Crock').
    - Mỗi pattern dùng \\b word boundary để khớp đúng ranh giới từ.
    """
    entries = []

    with open(csv_path, encoding='utf-8-sig', newline='') as f:
        reader = csv.reader(f)
        for row in reader:
            if len(row) < 2:
                continue

            en_text = row[0].strip()
            vi_text = row[1].strip()

            # Bỏ qua header
            if en_text.lower() in ('tiếng anh', 'english', 'en', 'source'):
                continue

            if not en_text or not vi_text or en_text == vi_text:
                continue

            try:
                # \b word boundary: chỉ khớp khi en_text là từ độc lập
                pattern = re.compile(
                    r'\b' + re.escape(en_text) + r'\b',
                    flags=re.IGNORECASE
                )
                entries.append((en_text, vi_text, pattern))
            except re.error:
                # Bỏ qua pattern không hợp lệ
                pass

    # Từ DÀI trước → tránh thay "Crock" trước khi xử lý "Crock Pot"
    entries.sort(key=lambda x: len(x[0]), reverse=True)

    logger.info(f"📖 Đã tải {len(entries):,} từ/cụm từ từ từ điển CSV")
    return entries


# ---------------------------------------------------------
# 2. Config helper
# ---------------------------------------------------------

def _get_conn_str_and_table(config_path: str) -> Tuple[str, str]:
    """Trả về (connection_string, table_name)."""
    with open(config_path, encoding='utf-8') as f:
        cfg = json.load(f)
    conn_str = (
        "DRIVER={ODBC Driver 17 for SQL Server};"
        "Server=.\\SQLEXPRESS;"
        "Database=ImportPOStringToDB;"
        "Trusted_Connection=yes;"
        "TrustServerCertificate=yes;"
    )
    table_name = cfg['database'].get('table_name', 'PoTranslations')
    return conn_str, table_name


# ---------------------------------------------------------
# 3. Apply tất cả replacement lên một chuỗi
# ---------------------------------------------------------

def apply_replacements(text: str,
                       entries: List[Tuple[str, str, re.Pattern]]) -> str:
    """
    Áp dụng tuần tự tất cả (pattern → vi_text) lên text.
    Dùng word boundary → không thay nhầm trong giữa từ.
    """
    for en_text, vi_text, pattern in entries:
        text = pattern.sub(vi_text, text)
    return text


# ---------------------------------------------------------
# 4. Worker: fetch → apply → update một chunk bản ghi
# ---------------------------------------------------------

def _worker(record_chunk: list,
            entries: List[Tuple[str, str, re.Pattern]],
            conn_str: str,
            table_name: str,
            dry_run: bool,
            worker_id: int,
            counter: Dict) -> None:
    """
    Mỗi luồng nhận một chunk bản ghi (MsgCtxt, MsgId, SuggestedTranslation).
    Áp dụng regex replacement và UPDATE bản ghi thay đổi.
    """
    local_changed = 0
    local_skipped = 0
    updates = []

    for rec in record_chunk:
        msg_ctxt  = rec['MsgCtxt']  or ''
        msg_id    = rec['MsgId']    or ''
        original  = rec['SuggestedTranslation'] or ''

        if not original.strip():
            local_skipped += 1
            continue

        new_text = apply_replacements(original, entries)

        if new_text == original:
            local_skipped += 1
            continue

        local_changed += 1
        updates.append((new_text, msg_ctxt, msg_id, original))

        if dry_run:
            with _print_lock:
                logger.info(
                    f"  [T{worker_id}] MsgId={msg_id!r:.40s}\n"
                    f"    Trước: {original[:80]!r}\n"
                    f"    Sau:   {new_text[:80]!r}"
                )

    # Ghi DB nếu không phải dry-run
    if not dry_run and updates:
        try:
            conn   = pyodbc.connect(conn_str)
            cursor = conn.cursor()
            sql = f"""
                UPDATE {table_name}
                SET SuggestedTranslation = ?
                WHERE MsgCtxt = ? AND MsgId = ?
                  AND (TranslationLocked IS NULL OR TranslationLocked != 1)
            """
            cursor.executemany(sql, [(u[0], u[1], u[2]) for u in updates])
            conn.commit()
            cursor.close()
            conn.close()
            with _print_lock:
                logger.info(f"  [T{worker_id}] ✅ Đã update {len(updates)} bản ghi")
        except Exception as e:
            with _print_lock:
                logger.error(f"  [T{worker_id}] ❌ Lỗi update: {e}")

    with _print_lock:
        counter['changed'] += local_changed
        counter['skipped'] += local_skipped


# ---------------------------------------------------------
# 5. Hàm chính
# ---------------------------------------------------------

def run(dry_run: bool = False, num_workers: int = DEFAULT_WORKERS):
    logger.info("=" * 60)
    logger.info("REPLACE IN SUGGESTED TRANSLATION (word boundary)")
    logger.info(f"   Mode:   {'DRY-RUN (khong ghi DB)' if dry_run else 'LIVE (ghi vao DB)'}")
    logger.info(f"   Luong:  {num_workers}")
    logger.info("=" * 60)

    # Bước 1: Tải từ điển
    entries = load_dictionary(CSV_FILE)
    if not entries:
        logger.error("❌ Từ điển rỗng.")
        return

    # Bước 2: Kết nối DB
    try:
        conn_str, table_name = _get_conn_str_and_table(CONFIG_FILE)
        conn = pyodbc.connect(conn_str)
        logger.info(f"✅ Kết nối DB thành công → bảng: {table_name}")
    except Exception as e:
        logger.error(f"❌ Không thể kết nối DB: {e}")
        return

    # Bước 3: Fetch toàn bộ bản ghi có SuggestedTranslation
    try:
        cursor = conn.cursor()
        cursor.execute(f"""
            SELECT MsgCtxt, MsgId, SuggestedTranslation
            FROM {table_name}
            WHERE SuggestedTranslation IS NOT NULL
              AND LTRIM(RTRIM(SuggestedTranslation)) != ''
              AND (TranslationLocked IS NULL OR TranslationLocked != 1)
        """)
        cols    = [c[0] for c in cursor.description]
        records = [dict(zip(cols, row)) for row in cursor.fetchall()]
        cursor.close()
        conn.close()
    except Exception as e:
        logger.error(f"❌ Lỗi fetch dữ liệu: {e}")
        conn.close()
        return

    logger.info(f"📋 Fetch được {len(records):,} bản ghi có SuggestedTranslation")

    if not records:
        logger.info("Không có bản ghi nào cần xử lý.")
        return

    # Bước 4: Chia bản ghi cho các luồng
    chunks  = [records[i::num_workers] for i in range(num_workers)]
    counter = {'changed': 0, 'skipped': 0}

    logger.info(f"\n🚀 Bắt đầu xử lý {len(records):,} bản ghi với {num_workers} luồng...\n")

    with ThreadPoolExecutor(max_workers=num_workers) as executor:
        futures = [
            executor.submit(_worker, chunk, entries, conn_str,
                            table_name, dry_run, wid, counter)
            for wid, chunk in enumerate(chunks, start=1)
            if chunk
        ]
        for f in as_completed(futures):
            f.result()

    logger.info("\n" + "=" * 60)
    label = "Sẽ thay đổi" if dry_run else "Đã cập nhật"
    logger.info("🎉 Hoàn thành!")
    logger.info(f"   {label}: {counter['changed']:,} bản ghi")
    logger.info(f"   Bỏ qua (không đổi): {counter['skipped']:,} bản ghi")
    logger.info("=" * 60)


# ---------------------------------------------------------
# 6. Show info
# ---------------------------------------------------------

def show_info():
    """Thống kê nhanh."""
    entries = load_dictionary(CSV_FILE)
    try:
        conn_str, table_name = _get_conn_str_and_table(CONFIG_FILE)
        conn   = pyodbc.connect(conn_str)
        cursor = conn.cursor()
    except Exception as e:
        logger.error(f"❌ Không thể kết nối: {e}")
        return

    try:
        cursor.execute(f"SELECT COUNT(*) FROM {table_name}")
        total = cursor.fetchone()[0]

        cursor.execute(f"""
            SELECT COUNT(*) FROM {table_name}
            WHERE SuggestedTranslation IS NOT NULL
              AND LTRIM(RTRIM(SuggestedTranslation)) != ''
        """)
        has_content = cursor.fetchone()[0]

        logger.info("\n" + "=" * 60)
        logger.info("📊 THỐNG KÊ:")
        logger.info(f"  📖 Từ điển CSV:              {len(entries):,} cụm từ")
        logger.info(f"  📝 Tổng bản ghi DB:          {total:,}")
        logger.info(f"  📝 Có SuggestedTranslation:  {has_content:,}")
        logger.info("=" * 60)

    finally:
        cursor.close()
        conn.close()


# ---------------------------------------------------------
# Entry point
# ---------------------------------------------------------

if __name__ == '__main__':
    args = sys.argv[1:]

    num_workers = DEFAULT_WORKERS
    if '--threads' in args:
        t_idx = args.index('--threads')
        try:
            num_workers = int(args[t_idx + 1])
        except (IndexError, ValueError):
            print("❌ Cú pháp: --threads N (VD: --threads 4)")
            sys.exit(1)

    if '--info' in args:
        show_info()
    elif '--dry-run' in args:
        run(dry_run=True, num_workers=num_workers)
    else:
        print("\n⚠️  Script sẽ cập nhật cột SuggestedTranslation trong DB!")
        print(f"   Số luồng: {num_workers}")
        ans = input("Bạn có chắc muốn tiếp tục? (y/n): ")
        if ans.strip().lower() == 'y':
            run(dry_run=False, num_workers=num_workers)
        else:
            print("Đã hủy. Dùng --dry-run để xem trước.")
