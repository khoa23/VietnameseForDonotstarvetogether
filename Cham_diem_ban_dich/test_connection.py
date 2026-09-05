import pyodbc
import json

def test_connection():
    # Đọc config
    with open('config.json', 'r', encoding='utf-8') as f:
        config = json.load(f)
    
    db_config = config['database']
    
    # Kiểm tra xem có connection_string không
    if 'connection_string' in db_config:
        connection_string = db_config['connection_string']
    else:
        # Tạo connection string từ các thành phần
        driver = db_config.get('driver', 'ODBC Driver 17 for SQL Server')
        server = db_config.get('server', '.\\SQLEXPRESS')
        database = db_config.get('database', 'ImportPOStringToDB')
        username = db_config.get('username', 'sa')
        password = db_config.get('password', '123456')
        
        connection_string = (
            f"DRIVER={{{driver}}};"
            f"SERVER={server};"
            f"DATABASE={database};"
            f"UID={username};"
            f"PWD={password};"
            f"TrustServerCertificate=yes;"
        )
    
    table_name = db_config.get('table_name', 'Translation')
    
    print("🔗 Đang kết nối...")
    print(f"📝 Connection string: {connection_string}")
    print(f"📋 Table name: {table_name}")
    print("-" * 60)
    
    try:
        conn = pyodbc.connect(connection_string)
        print("✅ Kết nối thành công!")
        
        # Kiểm tra bảng
        cursor = conn.cursor()
        cursor.execute(f"SELECT TOP 1 * FROM {table_name}")
        columns = [column[0] for column in cursor.description]
        print(f"📋 Các cột trong bảng: {', '.join(columns)}")
        
        # Đếm số bản ghi
        cursor.execute(f"SELECT COUNT(*) as Total FROM {table_name}")
        total = cursor.fetchone()[0]
        print(f"📊 Tổng số bản ghi: {total}")
        
        # Đếm số bản ghi có SuggestedTranslation
        cursor.execute(f"""
            SELECT COUNT(*) as HasSuggested 
            FROM {table_name} 
            WHERE SuggestedTranslation IS NOT NULL AND SuggestedTranslation != ''
        """)
        has_suggested = cursor.fetchone()[0]
        print(f"📊 Số bản ghi có SuggestedTranslation: {has_suggested}")
        
        # Đếm số bản ghi bị khóa
        cursor.execute(f"""
            SELECT COUNT(*) as Locked 
            FROM {table_name} 
            WHERE TranslationLocked = 1
        """)
        locked = cursor.fetchone()[0]
        print(f"🔒 Số bản ghi bị khóa: {locked}")
        
        cursor.close()
        conn.close()
        print("-" * 60)
        print("✅ Test hoàn tất!")
        
    except Exception as e:
        print(f"❌ Lỗi kết nối: {e}")
        print("\n💡 Gợi ý:")
        print("1. Kiểm tra SQL Server đang chạy")
        print("2. Kiểm tra tên database đúng")
        print("3. Kiểm tra username/password")
        print("4. Thử thay driver thành: SQL Server hoặc ODBC Driver 18 for SQL Server")

if __name__ == "__main__":
    test_connection()