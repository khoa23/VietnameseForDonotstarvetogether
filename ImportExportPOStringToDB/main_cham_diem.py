import json
import logging
import sys
import time
from typing import Dict, Any, List, Optional
from concurrent.futures import ThreadPoolExecutor, as_completed
from datetime import datetime
from database_helper import DatabaseHelper
from translation_selector import TranslationSelector

logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)

class TranslationProcessor:
    def __init__(self, config_path: str = 'config.json', max_workers: int = 4):
        self.db = DatabaseHelper(config_path)
        self.selector = TranslationSelector('dictionary.json')
        self.max_workers = max_workers
        self.stats = {
            'good': 0,
            'bad': 0,
            'error': 0,
            'no_suggested_good': 0,
            'no_suggested_bad': 0,
            'has_suggested_good': 0,
            'has_suggested_bad': 0,
            'locked_skipped': 0
        }
        
    def process_record(self, record: Dict[str, Any]) -> Dict[str, Any]:
        """
        Xử lý một bản ghi
        """
        try:
            msg_ctxt = record.get('MsgCtxt', '')
            msg_id = record.get('MsgId', '')
            msgstr = record.get('MsgStr', '')
            suggested = record.get('SuggestedTranslation', '')
            locked = record.get('TranslationLocked', 0)
            
            if locked == 1:
                return {'status': 'locked', 'msg_id': msg_id}
            
            # Sử dụng selector để đánh giá
            selected, rating = self.selector.select_translation(
                msgstr, 
                suggested, 
                msg_id
            )
            
            return {
                'status': 'processed',
                'msg_ctxt': msg_ctxt,
                'msg_id': msg_id,
                'rating': rating,
                'has_suggested': bool(suggested and suggested.strip()),
                'selected': selected
            }
            
        except Exception as e:
            logger.error(f"❌ Lỗi xử lý {record.get('MsgId', 'unknown')}: {e}")
            return {
                'status': 'error',
                'msg_id': record.get('MsgId', 'unknown'),
                'error': str(e)
            }
    
    def process_batch(self, records: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        """
        Xử lý một batch với đa luồng
        """
        results = []
        
        with ThreadPoolExecutor(max_workers=self.max_workers) as executor:
            future_to_record = {
                executor.submit(self.process_record, record): record 
                for record in records
            }
            
            for future in as_completed(future_to_record):
                try:
                    result = future.result()
                    results.append(result)
                except Exception as e:
                    record = future_to_record[future]
                    logger.error(f"❌ Lỗi không xử lý được {record.get('MsgId', 'unknown')}: {e}")
                    results.append({
                        'status': 'error',
                        'msg_id': record.get('MsgId', 'unknown'),
                        'error': str(e)
                    })
        
        return results
    
    def run(self, batch_size: int = 100):
        """
        Chạy xử lý chính
        """
        if not self.db.connect():
            logger.error("❌ Không thể kết nối database")
            return
        
        try:
            # Hiển thị thông tin
            self.show_info()
            
            # Lấy danh sách cần xử lý
            records = self.db.get_translations_to_process()
            
            if not records:
                logger.info("✅ Không có bản ghi nào cần xử lý")
                return
            
            total_records = len(records)
            logger.info(f"📝 Bắt đầu xử lý {total_records} bản ghi với {self.max_workers} luồng...")
            
            start_time = time.time()
            
            # Chia thành các batch nhỏ
            batches = [records[i:i + batch_size] for i in range(0, len(records), batch_size)]
            
            for batch_idx, batch in enumerate(batches, 1):
                logger.info(f"🔄 Đang xử lý batch {batch_idx}/{len(batches)} ({len(batch)} bản ghi)...")
                
                # Xử lý batch với đa luồng
                results = self.process_batch(batch)
                
                # Thống kê batch
                updates = []
                for result in results:
                    if result['status'] == 'processed':
                        if result['rating'] >= 1.0:
                            self.stats['good'] += 1
                            if result['has_suggested']:
                                self.stats['has_suggested_good'] += 1
                            else:
                                self.stats['no_suggested_good'] += 1
                        else:
                            self.stats['bad'] += 1
                            if result['has_suggested']:
                                self.stats['has_suggested_bad'] += 1
                            else:
                                self.stats['no_suggested_bad'] += 1
                        
                        updates.append({
                            'msg_ctxt': result['msg_ctxt'],
                            'msg_id': result['msg_id'],
                            'rating': result['rating']
                        })
                    elif result['status'] == 'locked':
                        self.stats['locked_skipped'] += 1
                    else:
                        self.stats['error'] += 1
                
                # Update database
                if updates:
                    self.db.batch_update(updates)
                
                # Hiển thị tiến độ
                processed = min(batch_idx * batch_size, total_records)
                progress = (processed / total_records) * 100
                logger.info(f"📊 Tiến độ: {processed}/{total_records} ({progress:.1f}%)")
            
            # Hiển thị kết quả
            elapsed_time = time.time() - start_time
            self.show_stats(elapsed_time)
            
        except Exception as e:
            logger.error(f"❌ Lỗi chính: {e}")
        finally:
            self.db.disconnect()
    
    def show_info(self):
        """Hiển thị thông tin bảng"""
        info = self.db.get_table_info()
        if info:
            logger.info("\n" + "="*60)
            logger.info("📊 THÔNG TIN BẢNG DỊCH:")
            logger.info(f"  📝 Tổng số bản ghi: {info.get('total', 0):,}")
            logger.info(f"  🔒 Bản ghi bị khóa: {info.get('locked', 0):,}")
            logger.info(f"  ✅ Đã xử lý (có LastUpdated): {info.get('processed', 0):,}")
            logger.info(f"  ⏳ Chờ xử lý (chưa có Rating): {info.get('pending', 0):,}")
            logger.info(f"  📝 Có SuggestedTranslation: {info.get('has_suggested', 0):,}")
            logger.info(f"  📝 Có Suggested + Chưa Rating: {info.get('pending_with_suggested', 0):,}")
            logger.info("="*60)
    
    def show_stats(self, elapsed_time: float):
        """Hiển thị thống kê"""
        total_processed = self.stats['good'] + self.stats['bad']
        
        logger.info("\n" + "="*50)
        logger.info("📊 THỐNG KÊ XỬ LÝ:")
        logger.info(f"  ✅ Tốt (rating 1.1): {self.stats['good']:,}")
        logger.info(f"  ❌ Không tốt (rating 0.5): {self.stats['bad']:,}")
        logger.info(f"  🔒 Bỏ qua (bị khóa): {self.stats['locked_skipped']:,}")
        logger.info(f"  ⚠️ Lỗi: {self.stats['error']:,}")
        logger.info(f"\n  📝 Chi tiết:")
        logger.info(f"     ├─ Không có Suggested: {self.stats['no_suggested_good'] + self.stats['no_suggested_bad']}")
        logger.info(f"     │  ├─ Tốt: {self.stats['no_suggested_good']}")
        logger.info(f"     │  └─ Không tốt: {self.stats['no_suggested_bad']}")
        logger.info(f"     └─ Có Suggested: {self.stats['has_suggested_good'] + self.stats['has_suggested_bad']}")
        logger.info(f"        ├─ Tốt: {self.stats['has_suggested_good']}")
        logger.info(f"        └─ Không tốt: {self.stats['has_suggested_bad']}")
        
        if elapsed_time > 0 and total_processed > 0:
            logger.info(f"\n  ⏱️ Thời gian xử lý: {elapsed_time:.2f} giây")
            logger.info(f"  🚀 Tốc độ: {total_processed / elapsed_time:.1f} bản ghi/giây")
        logger.info("="*50)

def dry_run():
    """Chạy thử không cập nhật database"""
    processor = TranslationProcessor(max_workers=1)
    
    if not processor.db.connect():
        logger.error("❌ Không thể kết nối database")
        return
    
    try:
        processor.show_info()
        records = processor.db.get_translations_to_process(limit=10)
        
        if records:
            logger.info(f"\n🧪 DRY RUN - XỬ LÝ 10 BẢN GHI ĐẦU TIÊN:")
            logger.info("="*60)
            
            for i, record in enumerate(records, 1):
                msg_id = record.get('MsgId', '')
                msgstr = record.get('MsgStr', '')
                suggested = record.get('SuggestedTranslation', '')
                
                if not suggested or suggested.strip() == '':
                    suggested_display = '(NULL)'
                else:
                    suggested_display = suggested[:50] + '...' if len(suggested) > 50 else suggested
                
                selected, rating = processor.selector.select_translation(
                    record['MsgStr'],
                    record.get('SuggestedTranslation', ''),
                    msg_id
                )
                
                status = "✅ GOOD" if rating >= 1.0 else "❌ BAD"
                logger.info(f"\n  [{i}] {msg_id[:40]}...")
                logger.info(f"    MsgStr: {msgstr[:60]}...")
                logger.info(f"    Suggested: {suggested_display}")
                logger.info(f"    Selected: {selected[:60]}...")
                logger.info(f"    Rating: {rating} - {status}")
                logger.info("-" * 40)
        
        processor.db.disconnect()
        
    except Exception as e:
        logger.error(f"❌ Lỗi dry run: {e}")
        processor.db.disconnect()

def show_info():
    """Hiển thị thông tin bảng"""
    db = DatabaseHelper('config.json')
    if db.connect():
        info = db.get_table_info()
        if info:
            logger.info("\n" + "="*60)
            logger.info("📊 THÔNG TIN BẢNG DỊCH:")
            logger.info(f"  📝 Tổng số bản ghi: {info.get('total', 0):,}")
            logger.info(f"  🔒 Bản ghi bị khóa: {info.get('locked', 0):,}")
            logger.info(f"  ✅ Đã xử lý (có LastUpdated): {info.get('processed', 0):,}")
            logger.info(f"  ⏳ Chờ xử lý (chưa có Rating): {info.get('pending', 0):,}")
            logger.info(f"  📝 Có SuggestedTranslation: {info.get('has_suggested', 0):,}")
            logger.info(f"  📝 Có Suggested + Chưa Rating: {info.get('pending_with_suggested', 0):,}")
            logger.info("="*60)
        db.disconnect()

def process_with_threads(num_threads: int):
    """Xử lý với số luồng chỉ định"""
    processor = TranslationProcessor(max_workers=num_threads)
    
    if processor.db.connect():
        pending = processor.db.get_pending_count()
        processor.db.disconnect()
        
        if pending == 0:
            print("✅ Không có bản ghi nào cần xử lý")
            return
        
        print(f"\n⚠️ BẠN SẮP CẬP NHẬT DATABASE")
        print(f"   Số bản ghi sẽ được xử lý: {pending:,}")
        print(f"   Số luồng: {num_threads}")
        response = input("Bạn có chắc muốn tiếp tục? (y/n): ")
        if response.lower() == 'y':
            processor.run()
        else:
            print("Đã hủy.")

if __name__ == "__main__":
    if len(sys.argv) > 1:
        if sys.argv[1] == '--dry-run':
            dry_run()
        elif sys.argv[1] == '--info':
            show_info()
        elif sys.argv[1] == '--threads' and len(sys.argv) > 2:
            try:
                num_threads = int(sys.argv[2])
                if num_threads < 1:
                    print("❌ Số luồng phải >= 1")
                else:
                    process_with_threads(num_threads)
            except ValueError:
                print("❌ Số luồng không hợp lệ")
        else:
            print("""
Usage:
  python main.py                    # Chạy xử lý chính (mặc định 4 luồng)
  python main.py --dry-run          # Chạy thử không update DB
  python main.py --info             # Xem thông tin bảng
  python main.py --threads N        # Chạy với N luồng (VD: --threads 8)
            """)
    else:
        process_with_threads(6)