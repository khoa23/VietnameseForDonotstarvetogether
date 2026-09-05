import json
import re

def extract_filtered_ids(json_file_path):
    """
    Đọc file JSON và xuất danh sách ID thỏa mãn điều kiện,
    ngăn cách bởi dấu phẩy
    """
    try:
        with open(json_file_path, 'r', encoding='utf-8') as f:
            data = json.load(f)
        
        filtered_ids = []
        
        for error in data:
            suggestion = error.get('suggestion', '')
            error_id = error.get('id')
            
            # Tìm pattern [word] và kiểm tra ký tự xung quanh
            pattern = r'([^\[]*?)\[([^\]]+)\]([^\]]*?)'
            matches = re.findall(pattern, suggestion)
            
            has_adjacent_char = False
            for before, word, after in matches:
                # Kiểm tra có ký tự ngay trước [ hoặc ngay sau ]
                if (before and before[-1] != ' ' and before[-1] != '') or (after and after[0] != ' ' and after[0] != ''):
                    has_adjacent_char = True
                    break
            
            if has_adjacent_char:
                filtered_ids.append(str(error_id))
        
        # Xuất danh sách ID, ngăn cách bởi dấu phẩy
        result = ", ".join(filtered_ids)
        print(result)
        
        # In thêm thông tin
        print(f"\nTổng số: {len(filtered_ids)} IDs")
        
        return filtered_ids
        
    except FileNotFoundError:
        print(f"Không tìm thấy file: {json_file_path}")
        return []
    except json.JSONDecodeError:
        print(f"File {json_file_path} không đúng định dạng JSON")
        return []

if __name__ == "__main__":
    json_file = "spelling_errors.json"
    extract_filtered_ids(json_file)