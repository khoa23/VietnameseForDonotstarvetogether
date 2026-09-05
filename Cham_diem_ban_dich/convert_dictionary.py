import csv
import json
from collections import defaultdict

def convert_csv_to_dictionary(csv_file_path: str, output_json_path: str = 'dictionary.json'):
    """
    Chuyển đổi file CSV từ điển tiếng Anh - Tiếng Việt sang định dạng JSON
    """
    # Đọc file CSV
    with open(csv_file_path, 'r', encoding='utf-8') as f:
        reader = csv.reader(f)
        header = next(reader)  # Bỏ qua header
        
        # Tạo dictionary để lưu
        game_terms = defaultdict(lambda: {'en': '', 'vn': [], 'context': ''})
        
        for row in reader:
            if len(row) < 2:
                continue
            
            en_text = row[0].strip()
            vn_text = row[1].strip()
            
            if not en_text or not vn_text:
                continue
            
            # Lấy từ khóa từ tiếng Anh (lấy từ đầu tiên hoặc toàn bộ)
            # Chuyển thành dạng key cho dictionary
            en_lower = en_text.lower()
            
            # Tạo key từ tiếng Anh (loại bỏ dấu câu, lấy từ đầu tiên hoặc toàn bộ)
            key = en_lower.replace('"', '').replace("'", '').replace('?', '').replace('!', '').strip()
            
            # Nếu key rỗng hoặc quá dài, lấy từ đầu tiên
            if not key or len(key) > 30:
                # Lấy từ đầu tiên
                key = key.split()[0] if key.split() else key
            
            # Lưu vào game_terms
            if not game_terms[key]['en']:
                game_terms[key]['en'] = en_text
            
            # Thêm bản dịch tiếng Việt nếu chưa có
            if vn_text not in game_terms[key]['vn']:
                game_terms[key]['vn'].append(vn_text)
            
            # Thêm context (nếu có)
            if not game_terms[key]['context']:
                game_terms[key]['context'] = f"Translation of {en_text[:30]}..."
    
    # Chuyển defaultdict thành dict thường
    game_terms_dict = dict(game_terms)
    
    # Tạo dictionary cuối cùng
    dictionary_data = {
        "game_terms": game_terms_dict,
        "typo_fixes": {
            "ròi": "rơi",
            "gó": "gió",
            "điểm tỉnh": "tỉnh táo",
            "memm": "merm",
            "nón": "mũ",
            "váy": "áo",
            "giáp": "áo giáp"
        },
        "bad_translations": [
            "bò ròi",
            "memm",
            "gó",
            "không có gì",
            "không rõ"
        ]
    }
    
    # Lưu thành file JSON
    with open(output_json_path, 'w', encoding='utf-8') as f:
        json.dump(dictionary_data, f, ensure_ascii=False, indent=4)
    
    print(f"✅ Đã tạo file {output_json_path} với {len(game_terms_dict)} từ điển")
    print(f"📊 Tổng số từ trong từ điển: {sum(len(v['vn']) for v in game_terms_dict.values())} bản dịch")
    
    # Thống kê
    print("\n📊 THỐNG KÊ TỪ ĐIỂN:")
    print(f"  - Số lượng từ gốc: {len(game_terms_dict)}")
    print(f"  - Tổng số bản dịch: {sum(len(v['vn']) for v in game_terms_dict.values())}")
    
    return dictionary_data

def create_enhanced_dictionary():
    """
    Tạo từ điển nâng cao với các trường hợp đặc biệt cho Don't Starve
    """
    enhanced_dict = {
        "game_terms": {
            # Thêm các từ đặc biệt cho Don't Starve
            "abigail": {"en": "Abigail", "vn": ["Abigail"], "context": "Character"},
            "abigail_flower": {"en": "Abigail's Flower", "vn": ["Hoa của Abigail"], "context": "Item"},
            "deerclops": {"en": "Deerclops", "vn": ["Deerclops", "Quái vật Băng"], "context": "Boss"},
            "bearger": {"en": "Bearger", "vn": ["Gấu Lửng", "Bearger"], "context": "Boss"},
            "dragonfly": {"en": "Dragonfly", "vn": ["Dragonfly", "Ruồi Rồng"], "context": "Boss"},
            "moose": {"en": "Moose/Goose", "vn": ["Hươu/Ngỗng", "Moose/Goose"], "context": "Boss"},
            "klaus": {"en": "Klaus", "vn": ["Klaus", "Ông già Noel"], "context": "Boss"},
            "toadstool": {"en": "Toadstool", "vn": ["Cóc Nấm", "Toadstool"], "context": "Boss"},
            "bee_queen": {"en": "Bee Queen", "vn": ["Ong Chúa", "Bee Queen"], "context": "Boss"},
            "shadow_piece": {"en": "Shadow Piece", "vn": ["Mảnh Bóng Tối"], "context": "Boss"},
            "ancient_guardian": {"en": "Ancient Guardian", "vn": ["Quản Thần Cổ Đại"], "context": "Boss"},
            "fuelweaver": {"en": "Ancient Fuelweaver", "vn": ["Thượng Cổ Hắc Ám"], "context": "Boss"},
            "celestial_champion": {"en": "Celestial Champion", "vn": ["Chiến Binh Thiên Hà"], "context": "Boss"},
            
            # Thêm các vật phẩm quan trọng
            "thulecite": {"en": "Thulecite", "vn": ["Thulecite"], "context": "Material"},
            "nightmare_fuel": {"en": "Nightmare Fuel", "vn": ["Nhiên liệu Ác Mộng"], "context": "Material"},
            "living_log": {"en": "Living Log", "vn": ["Gỗ sống"], "context": "Material"},
            "marble": {"en": "Marble", "vn": ["Đá cẩm thạch"], "context": "Material"},
            
            # Thêm các công thức nấu ăn
            "meatballs": {"en": "Meatballs", "vn": ["Thịt viên"], "context": "Food"},
            "honey_ham": {"en": "Honey Ham", "vn": ["Đùi lợn ướp mật"], "context": "Food"},
            "bacon_and_eggs": {"en": "Bacon and Eggs", "vn": ["Thịt Xông Khói và Trứng"], "context": "Food"},
            "pierogi": {"en": "Pierogi", "vn": ["Sủi Cảo"], "context": "Food"},
            "dragonpie": {"en": "Dragonpie", "vn": ["Bánh Thanh Long"], "context": "Food"},
            
            # Thêm các thuật ngữ game
            "krampus": {"en": "Krampus", "vn": ["Krampus"], "context": "Character"},
            "chester": {"en": "Chester", "vn": ["Chester"], "context": "Character"},
            "glommer": {"en": "Glommer", "vn": ["Glommer"], "context": "Character"},
        },
        "typo_fixes": {
            "ròi": "rơi",
            "gó": "gió",
            "điểm tỉnh": "tỉnh táo",
            "memm": "merm",
            "nón": "mũ",
            "váy": "áo",
            "giáp": "áo giáp",
            "bò ròi": "bỏ rơi",
            "bò tàu": "bỏ tàu",
            "du hành": "thám hiểm",
            "cá nhân": "người cá"
        },
        "bad_translations": [
            "bò ròi",
            "memm",
            "gó",
            "không có gì",
            "không rõ",
            "null",
            "none"
        ]
    }
    
    # Lưu thành file JSON
    with open('dictionary.json', 'w', encoding='utf-8') as f:
        json.dump(enhanced_dict, f, ensure_ascii=False, indent=4)
    
    print("✅ Đã tạo file dictionary.json với từ điển nâng cao")
    print(f"📊 Tổng số từ trong từ điển: {len(enhanced_dict['game_terms'])}")

def merge_dictionaries(csv_path: str, output_path: str = 'dictionary.json'):
    """
    Gộp từ điển từ CSV và từ điển nâng cao
    """
    # Chuyển đổi từ CSV
    csv_dict = convert_csv_to_dictionary(csv_path, 'dictionary_temp.json')
    
    # Tạo từ điển nâng cao
    enhanced_dict = {
        "game_terms": {},
        "typo_fixes": {
            "ròi": "rơi",
            "gó": "gió",
            "điểm tỉnh": "tỉnh táo",
            "memm": "merm",
            "nón": "mũ",
            "váy": "áo",
            "giáp": "áo giáp",
            "bò ròi": "bỏ rơi",
            "bò tàu": "bỏ tàu",
            "du hành": "thám hiểm",
            "cá nhân": "người cá"
        },
        "bad_translations": [
            "bò ròi",
            "memm",
            "gó",
            "không có gì",
            "không rõ",
            "null",
            "none"
        ]
    }
    
    # Gộp từ điển từ CSV
    for key, value in csv_dict['game_terms'].items():
        if key not in enhanced_dict['game_terms']:
            enhanced_dict['game_terms'][key] = value
    
    # Lưu thành file JSON
    with open(output_path, 'w', encoding='utf-8') as f:
        json.dump(enhanced_dict, f, ensure_ascii=False, indent=4)
    
    print(f"✅ Đã tạo file {output_path} với {len(enhanced_dict['game_terms'])} từ điển")
    
    # Làm sạch file tạm
    import os
    if os.path.exists('dictionary_temp.json'):
        os.remove('dictionary_temp.json')

if __name__ == "__main__":
    # Đường dẫn file CSV
    csv_file = 'strings_out_header.csv'
    
    try:
        # Tạo từ điển từ CSV
        merge_dictionaries(csv_file, 'dictionary.json')
        print("\n✅ THÀNH CÔNG! File dictionary.json đã được tạo.")
        print("📝 Bạn có thể sử dụng file này với translation_selector.py")
    except FileNotFoundError:
        print(f"❌ Không tìm thấy file {csv_file}")
        print("📝 Đang tạo từ điển cơ bản...")
        create_enhanced_dictionary()
    except Exception as e:
        print(f"❌ Lỗi: {e}")
        print("📝 Đang tạo từ điển cơ bản...")
        create_enhanced_dictionary()