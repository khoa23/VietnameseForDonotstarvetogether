"""
fill_suggested_from_csv.py
--------------------------
Tra từ điển trong strings_out_header.csv và điền vào cột SuggestedTranslation
trong database nếu MsgId khớp với cột "Tiếng Anh" của file CSV.

Cột CSV:
  Cột 1 (Tiếng Anh)  → dùng để khớp với MsgId trong database
  Cột 2 (Tiếng Việt) → điền vào SuggestedTranslation nếu chưa có

Cách dùng:
  python fill_suggested_from_csv.py             # Chỉ điền khi SuggestedTranslation đang NULL/rỗng
  python fill_suggested_from_csv.py --overwrite # Ghi đè cả bản ghi đã có SuggestedTranslation
  python fill_suggested_from_csv.py --dry-run   # Thử không ghi vào DB
  python fill_suggested_from_csv.py --info      # Chỉ xem thống kê
"""

import csv
import json
import logging
import sys
import pyodbc
from typing import Dict, Tuple

logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)

CSV_FILE    = 'strings_out_header.csv'
CONFIG_FILE = 'config.json'


# ---------------------------------------------------------
# 1. Đọc từ điển CSV
# ---------------------------------------------------------

def load_dictionary(csv_path: str) -> Dict[str, str]:
    """
    Đọc file CSV (cột 1 = Tiếng Anh, cột 2 = Tiếng Việt).
    Trả về dict { tiếng_anh: tiếng_việt }.
    """
    dictionary: Dict[str, str] = {}

    with open(csv_path, encoding='utf-8-sig', newline='') as f:
        reader = csv.reader(f)
        for row_num, row in enumerate(reader, start=1):
            if len(row) < 2:
                continue

            en_text = row[0].strip()
            vi_text = row[1].strip()

            # Bỏ qua dòng header
            if en_text.lower() in ('tiếng anh', 'english', 'en', 'source'):
                logger.info(f"  Dòng {row_num}: bỏ qua header '{en_text}'")
                continue

            if en_text and vi_text:
                dictionary[en_text] = vi_text

    logger.info(f"📖 Đã tải {len(dictionary):,} từ/cụm từ từ từ điển CSV")
    return dictionary


# ---------------------------------------------------------
# 2. Kết nối database
# ---------------------------------------------------------

def get_connection_string(config_path: str) -> Tuple[str, str]:
    """Trả về (connection_string, table_name) từ config.json."""
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
# 3. Lấy danh sách bản ghi cần điền
# ---------------------------------------------------------

def fetch_records(cursor, table_name: str, overwrite: bool):
    """
    Lấy (MsgCtxt, MsgId) từ DB.
    overwrite=False → chỉ lấy bản ghi chưa có SuggestedTranslation.
    overwrite=True  → lấy tất cả bản ghi không bị khóa.
    """
    if overwrite:
        query = f"""
            SELECT MsgCtxt, MsgId, SuggestedTranslation
            FROM {table_name}
            WHERE (TranslationLocked IS NULL OR TranslationLocked != 1)
        """
    else:
        query = f"""
            SELECT MsgCtxt, MsgId, SuggestedTranslation
            FROM {table_name}
            WHERE (TranslationLocked IS NULL OR TranslationLocked != 1)
              AND (SuggestedTranslation IS NULL OR LTRIM(RTRIM(SuggestedTranslation)) = '')
        """

    cursor.execute(query)
    columns = [col[0] for col in cursor.description]
    rows = cursor.fetchall()
    return [dict(zip(columns, row)) for row in rows]


# ---------------------------------------------------------
# 4. Batch update
# ---------------------------------------------------------

def batch_update(cursor, conn, table_name: str, updates: list, dry_run: bool) -> int:
    """
    Cập nhật SuggestedTranslation cho các bản ghi khớp.
    Trả về số bản ghi đã cập nhật.
    """
    if not updates:
        return 0

    if dry_run:
        logger.info(f"[DRY-RUN] Sẽ cập nhật {len(updates)} bản ghi (không ghi vào DB)")
        for u in updates[:10]:
            logger.info(f"  MsgId={u['msg_id']!r:40s}  -> {u['vi_text']!r}")
        if len(updates) > 10:
            logger.info(f"  ... và {len(updates) - 10} bản ghi khác")
        return len(updates)

    sql = f"""
        UPDATE {table_name}
        SET SuggestedTranslation = ?
        WHERE MsgCtxt = ? AND MsgId = ?
          AND (TranslationLocked IS NULL OR TranslationLocked != 1)
    """

    data = [(u['vi_text'], u['msg_ctxt'], u['msg_id']) for u in updates]

    try:
        cursor.executemany(sql, data)
        conn.commit()
        logger.info(f"✅ Đã cập nhật {len(updates):,} bản ghi")
        return len(updates)
    except Exception as e:
        logger.error(f"❌ Lỗi khi update: {e}")
        conn.rollback()
        return 0


# ---------------------------------------------------------
# 5. Hàm chính
# ---------------------------------------------------------

def run(overwrite: bool = False, dry_run: bool = False):
    logger.info("=" * 60)
    logger.info("FILL SUGGESTED TRANSLATION FROM CSV")
    logger.info(f"   Mode: {'DRY-RUN' if dry_run else 'LIVE'}")
    logger.info(f"   Overwrite: {'Co (ghi de ca ban ghi cu)' if overwrite else 'Khong (chi NULL/rong)'}")
    logger.info("=" * 60)

    # Bước 1: Tải từ điển
    dictionary = load_dictionary(CSV_FILE)
    if not dictionary:
        logger.error("❌ Từ điển rỗng, dừng lại.")
        return

    # Bước 2: Kết nối DB
    conn_str, table_name = get_connection_string(CONFIG_FILE)
    try:
        conn = pyodbc.connect(conn_str)
        cursor = conn.cursor()
        logger.info(f"✅ Kết nối database thành công -> bảng: {table_name}")
    except Exception as e:
        logger.error(f"❌ Không thể kết nối database: {e}")
        return

    try:
        # Bước 3: Lấy danh sách bản ghi
        records = fetch_records(cursor, table_name, overwrite)
        logger.info(f"📋 Lấy được {len(records):,} bản ghi từ DB cần kiểm tra")

        if not records:
            logger.info("✅ Không có bản ghi nào cần xử lý.")
            return

        # Bước 4: Tra từ điển
        matched   = []
        not_found = 0

        for rec in records:
            msg_id   = (rec.get('MsgId')   or '').strip()
            msg_ctxt = (rec.get('MsgCtxt') or '').strip()

            vi_text = dictionary.get(msg_id)

            if vi_text:
                matched.append({
                    'msg_ctxt': msg_ctxt,
                    'msg_id':   msg_id,
                    'vi_text':  vi_text,
                })
            else:
                not_found += 1

        logger.info(f"\n📊 Kết quả tra từ điển:")
        logger.info(f"   ✅ Khớp được:   {len(matched):,} bản ghi")
        logger.info(f"   ❌ Không khớp:  {not_found:,} bản ghi")

        if not matched:
            logger.info("Không có bản ghi nào cần cập nhật.")
            return

        # Bước 5: Cập nhật DB theo batch 500
        BATCH = 500
        total_updated = 0
        for i in range(0, len(matched), BATCH):
            chunk = matched[i:i + BATCH]
            logger.info(f"🔄 Batch {i // BATCH + 1}: {len(chunk)} bản ghi...")
            total_updated += batch_update(cursor, conn, table_name, chunk, dry_run)

        label = "sẽ cập nhật" if dry_run else "đã cập nhật"
        logger.info(f"\n🎉 Hoàn thành! Tổng cộng {label} {total_updated:,} bản ghi SuggestedTranslation.")

    finally:
        cursor.close()
        conn.close()
        logger.info("🔌 Đã ngắt kết nối database.")


def show_info():
    """Chỉ xem thống kê, không cập nhật."""
    dictionary = load_dictionary(CSV_FILE)
    conn_str, table_name = get_connection_string(CONFIG_FILE)

    try:
        conn = pyodbc.connect(conn_str)
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
        has_suggested = cursor.fetchone()[0]

        cursor.execute(f"""
            SELECT COUNT(*) FROM {table_name}
            WHERE (TranslationLocked IS NULL OR TranslationLocked != 1)
              AND (SuggestedTranslation IS NULL OR LTRIM(RTRIM(SuggestedTranslation)) = '')
        """)
        no_suggested = cursor.fetchone()[0]

        # Ước tính bao nhiêu bản ghi có thể fill
        records_no_sug = fetch_records(cursor, table_name, overwrite=False)
        can_fill = sum(
            1 for r in records_no_sug
            if dictionary.get((r.get('MsgId') or '').strip())
        )

        logger.info("\n" + "=" * 60)
        logger.info("📊 THỐNG KÊ:")
        logger.info(f"  📖 Từ điển CSV:               {len(dictionary):,} từ")
        logger.info(f"  📝 Tổng bản ghi DB:           {total:,}")
        logger.info(f"  ✅ Đã có SuggestedTranslation: {has_suggested:,}")
        logger.info(f"  ⏳ Chưa có (không khóa):      {no_suggested:,}")
        logger.info(f"  🎯 Có thể điền được:          {can_fill:,}")
        logger.info("=" * 60)

    finally:
        cursor.close()
        conn.close()


# ---------------------------------------------------------
# Entry point
# ---------------------------------------------------------

if __name__ == '__main__':
    args = sys.argv[1:]

    if '--info' in args:
        show_info()
    elif '--dry-run' in args:
        overwrite = '--overwrite' in args
        run(overwrite=overwrite, dry_run=True)
    elif '--overwrite' in args:
        print("\n⚠️  CHE DO OVERWRITE: Se ghi de ca ban ghi da co SuggestedTranslation!")
        ans = input("Ban co chac muon tiep tuc? (y/n): ")
        if ans.strip().lower() == 'y':
            run(overwrite=True, dry_run=False)
        else:
            print("Da huy.")
    else:
        run(overwrite=False, dry_run=False)
