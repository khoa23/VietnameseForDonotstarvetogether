from database_helper import DatabaseHelper
import logging

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

def reset_all_processed():
    """
    Reset tất cả các bản ghi đã xử lý về trạng thái chờ xử lý lại
    """
    db = DatabaseHelper('config.json')
    
    if not db.connect():
        logger.error("❌ Không thể kết nối database")
        return
    
    try:
        # Đếm số bản ghi đã xử lý
        db.cursor.execute(f"""
            SELECT COUNT(*) as Count 
            FROM {db.table_name} 
            WHERE LastUpdated IS NOT NULL
              AND (TranslationLocked IS NULL OR TranslationLocked != 1)
        """)
        processed_count = db.cursor.fetchone()[0]
        
        if processed_count == 0:
            logger.info("✅ Không có bản ghi nào đã xử lý")
            db.disconnect()
            return
        
        logger.info(f"📊 Tìm thấy {processed_count:,} bản ghi đã xử lý")
        
        # Xác nhận
        response = input(f"⚠️ Bạn có chắc muốn RESET {processed_count:,} bản ghi? (y/n): ")
        if response.lower() != 'y':
            logger.info("Đã hủy")
            db.disconnect()
            return
        
        # Reset LastUpdated và Rating
        db.cursor.execute(f"""
            UPDATE {db.table_name}
            SET LastUpdated = NULL,
                Rating = NULL
            WHERE LastUpdated IS NOT NULL
              AND (TranslationLocked IS NULL OR TranslationLocked != 1)
        """)
        db.conn.commit()
        
        logger.info(f"✅ Đã reset {processed_count:,} bản ghi")
        
        # Hiển thị thông tin mới
        db.cursor.execute(f"SELECT COUNT(*) FROM {db.table_name} WHERE LastUpdated IS NULL")
        pending = db.cursor.fetchone()[0]
        logger.info(f"📊 Số bản ghi chờ xử lý: {pending:,}")
        
    except Exception as e:
        logger.error(f"❌ Lỗi: {e}")
        db.conn.rollback()
    finally:
        db.disconnect()

if __name__ == "__main__":
    reset_all_processed()