import pyodbc
import json
from typing import List, Dict, Any, Optional
import logging
import threading

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

class DatabaseHelper:
    def __init__(self, config_path: str = 'config.json'):
        with open(config_path, 'r', encoding='utf-8') as f:
            self.config = json.load(f)
        
        self.connection_string = (
            "DRIVER={ODBC Driver 17 for SQL Server};"
            "Server=.\\SQLEXPRESS;"
            "Database=ImportPOStringToDB;"
            "Trusted_Connection=yes;"
            "TrustServerCertificate=yes;"
        )
        
        self.table_name = self.config['database'].get('table_name', 'PoTranslations')
        self.conn = None
        self.cursor = None
        self._lock = threading.Lock()
        
        logger.info(f"🔗 Đã tải cấu hình database: {self.table_name}")
        
    def connect(self):
        """Kết nối đến SQL Server"""
        try:
            self.conn = pyodbc.connect(self.connection_string)
            self.cursor = self.conn.cursor()
            logger.info("✅ Kết nối database thành công")
            return True
        except Exception as e:
            logger.error(f"❌ Lỗi kết nối: {e}")
            return False
    
    def disconnect(self):
        """Ngắt kết nối"""
        if self.cursor:
            self.cursor.close()
        if self.conn:
            self.conn.close()
        logger.info("Đã ngắt kết nối database")
    
    def get_translations_to_process(self, limit: Optional[int] = None) -> List[Dict[str, Any]]:
        """
        Lấy các bản ghi cần xử lý
        BỎ QUA LastUpdated - xử lý tất cả bản ghi chưa có Rating
        """
        query = f"""
            SELECT 
                MsgCtxt,
                MsgId,
                MsgStr,
                SuggestedTranslation,
                Rating,
                TranslationLocked,
                LastUpdated
            FROM {self.table_name}
            WHERE (TranslationLocked IS NULL OR TranslationLocked != 1)
              AND (Rating IS NULL OR Rating = 0)
        """
        
        if limit:
            query += f" TOP {limit}"
        
        try:
            self.cursor.execute(query)
            columns = [column[0] for column in self.cursor.description]
            rows = self.cursor.fetchall()
            
            result = []
            for row in rows:
                result.append(dict(zip(columns, row)))
            
            has_suggested = sum(1 for r in result if r.get('SuggestedTranslation'))
            no_suggested = len(result) - has_suggested
            
            logger.info(f"📊 Tìm thấy {len(result)} bản ghi cần xử lý")
            logger.info(f"   ├─ Có SuggestedTranslation: {has_suggested}")
            logger.info(f"   └─ Không có SuggestedTranslation: {no_suggested}")
            
            return result
        except Exception as e:
            logger.error(f"❌ Lỗi truy vấn: {e}")
            return []
    
    def get_locked_count(self) -> int:
        """Đếm số bản ghi bị khóa"""
        query = f"""
            SELECT COUNT(*) as LockedCount
            FROM {self.table_name}
            WHERE TranslationLocked = 1
        """
        
        try:
            self.cursor.execute(query)
            result = self.cursor.fetchone()
            return result[0] if result else 0
        except Exception as e:
            logger.error(f"❌ Lỗi đếm bản ghi khóa: {e}")
            return 0
    
    def get_pending_count(self) -> int:
        """Đếm số bản ghi chờ xử lý (chưa có Rating) - BỎ QUA LastUpdated"""
        query = f"""
            SELECT COUNT(*) as PendingCount
            FROM {self.table_name}
            WHERE (TranslationLocked IS NULL OR TranslationLocked != 1)
              AND (Rating IS NULL OR Rating = 0)
        """
        
        try:
            self.cursor.execute(query)
            result = self.cursor.fetchone()
            return result[0] if result else 0
        except Exception as e:
            logger.error(f"❌ Lỗi đếm bản ghi chờ: {e}")
            return 0
    
    def batch_update(self, updates: List[Dict[str, Any]]) -> bool:
        """
        Cập nhật nhiều bản ghi cùng lúc (Thread-safe)
        """
        if not updates:
            return True
        
        with self._lock:
            query = f"""
                UPDATE {self.table_name}
                SET Rating = ?,
                    LastUpdated = GETDATE()
                WHERE MsgCtxt = ? AND MsgId = ?
                  AND (TranslationLocked IS NULL OR TranslationLocked != 1)
            """
            
            try:
                data = [(u['rating'], u['msg_ctxt'], u['msg_id']) for u in updates]
                self.cursor.executemany(query, data)
                self.conn.commit()
                logger.info(f"✅ Đã cập nhật {len(updates)} bản ghi")
                return True
            except Exception as e:
                logger.error(f"❌ Lỗi batch update: {e}")
                self.conn.rollback()
                return False
    
    def get_table_info(self) -> Dict[str, Any]:
        """Lấy thông tin về bảng"""
        try:
            # Tổng số
            self.cursor.execute(f"SELECT COUNT(*) as Total FROM {self.table_name}")
            total = self.cursor.fetchone()[0]
            
            # Bị khóa
            locked = self.get_locked_count()
            
            # Đã xử lý (có LastUpdated)
            self.cursor.execute(f"""
                SELECT COUNT(*) as Processed 
                FROM {self.table_name} 
                WHERE LastUpdated IS NOT NULL
            """)
            processed = self.cursor.fetchone()[0]
            
            # Chờ xử lý (chưa có Rating) - BỎ QUA LastUpdated
            pending = self.get_pending_count()
            
            # Có SuggestedTranslation
            self.cursor.execute(f"""
                SELECT COUNT(*) as HasSuggested 
                FROM {self.table_name} 
                WHERE SuggestedTranslation IS NOT NULL AND SuggestedTranslation != ''
            """)
            has_suggested = self.cursor.fetchone()[0]
            
            # Có Suggested và chưa có Rating
            self.cursor.execute(f"""
                SELECT COUNT(*) as PendingWithSuggested 
                FROM {self.table_name} 
                WHERE SuggestedTranslation IS NOT NULL 
                  AND SuggestedTranslation != ''
                  AND (Rating IS NULL OR Rating = 0)
                  AND (TranslationLocked IS NULL OR TranslationLocked != 1)
            """)
            pending_with_suggested = self.cursor.fetchone()[0]
            
            return {
                'total': total,
                'locked': locked,
                'processed': processed,
                'pending': pending,
                'has_suggested': has_suggested,
                'pending_with_suggested': pending_with_suggested
            }
        except Exception as e:
            logger.error(f"❌ Lỗi lấy thông tin bảng: {e}")
            return {}