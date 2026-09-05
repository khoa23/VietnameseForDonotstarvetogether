import pyodbc
import json
import re
from typing import List, Dict, Tuple, Optional
from dataclasses import dataclass
from underthesea import word_tokenize, pos_tag, sent_tokenize
import warnings
warnings.filterwarnings('ignore')

@dataclass
class SpellingError:
    id: int
    original_text: str
    keyword: str
    full_word: str
    error_type: str
    suggestion: str
    
class VietnameseSpellChecker:
    def __init__(self, config_path: str = 'config.json'):
        """Initialize spell checker with database connection"""
        self.config = self._load_config(config_path)
        self.conn = self._connect_db()
        
        # Load Vietnamese dictionary (words that should exist)
        self.vietnamese_words = self._load_vietnamese_dictionary()
        
    def _load_config(self, config_path: str) -> dict:
        """Load configuration from JSON file"""
        with open(config_path, 'r', encoding='utf-8') as f:
            return json.load(f)
    
    def _connect_db(self):
        """Connect to SQL Server database"""
        conn_str = self.config['database']['connection_string']
        return pyodbc.connect(conn_str)
    
    def _load_vietnamese_dictionary(self) -> set:
        """Load Vietnamese words from database dictionary table"""
        try:
            cursor = self.conn.cursor()
            # Assuming you have a dictionary table 'tu_dien'
            cursor.execute("SELECT DISTINCT tiengviet FROM tu_dien")
            words = {row[0].lower() for row in cursor.fetchall()}
            print(f"Loaded {len(words)} Vietnamese words from dictionary")
            return words
        except Exception as e:
            print(f"Warning: Could not load dictionary: {e}")
            # Fallback to common Vietnamese words
            return {
                'beefalo', 'wolfgang', 'wendy', 'wickerbottom',
                'và', 'của', 'cho', 'với', 'trong', 'trên', 'dưới',
                'là', 'không', 'có', 'được', 'sẽ', 'đã', 'đang'
            }
    
    def extract_word_containing_keyword(self, text: str, keyword: str) -> Optional[str]:
        """Extract the full word that contains the keyword"""
        pos = text.lower().find(keyword.lower())
        if pos == -1:
            return None
            
        # Find word boundaries
        start = pos
        while start > 0 and text[start-1].isalpha():
            start -= 1
            
        end = pos + len(keyword)
        while end < len(text) and text[end].isalpha():
            end += 1
            
        return text[start:end]
    
    def is_valid_vietnamese_word(self, word: str) -> bool:
        """Check if a word is valid Vietnamese"""
        # Remove non-alphabetic characters
        clean_word = re.sub(r'[^a-zA-ZÀ-ỹ\s]', '', word.lower())
        if not clean_word:
            return False
        return clean_word in self.vietnamese_words
    
    def check_text(self, text: str, dictionary_words: List[str]) -> List[Dict]:
        """Check text for spelling errors"""
        errors = []
        
        for dict_word in dictionary_words:
            if len(dict_word) < 3:
                continue
            
            # Escape dict_word phòng trường hợp chứa ký tự đặc biệt
            escaped_kw = re.escape(dict_word)
            
            # Tìm keyword khi nó bị dính ký tự chữ/số (\w) ở đằng trước HOẶC đằng sau
            pattern = re.compile(rf'(\w+{escaped_kw}|{escaped_kw}\w+|\w+{escaped_kw}\w+)', re.IGNORECASE)
            
            for match in pattern.finditer(text):
                full_word = match.group(0)
                
                # Tránh trùng lặp nếu đã quét lỗi này
                if not any(e['full_word'] == full_word and e['keyword'] == dict_word for e in errors):
                    errors.append({
                        'full_word': full_word,
                        'keyword': dict_word,
                        'error_type': 'embedded_keyword',
                        'suggestion': self._generate_suggestion(full_word, dict_word)
                    })
                
        return errors

    def _generate_suggestion(self, full_word: str, keyword: str) -> str:
        """Generate suggestion with exact case-insensitive replacement"""
        # Thay thế giữ nguyên casing của keyword trong từ bị lỗi
        pattern = re.compile(re.escape(keyword), re.IGNORECASE)
        return pattern.sub(f'[{keyword}]', full_word)
    
    def get_errors_from_database(self) -> List[SpellingError]:
        """Get all translation records with spelling errors"""
        cursor = self.conn.cursor()
        
        # Get dictionary words
        cursor.execute("SELECT tiengviet FROM tu_dien")
        dict_words = [row[0] for row in cursor.fetchall()]
        
        # Get translations to check
        cursor.execute("SELECT ID, SuggestedTranslation FROM PoTranslations")
        records = cursor.fetchall()
        
        errors = []
        total = len(records)
        
        print(f"Checking {total} translations for errors...")
        
        for idx, (record_id, text) in enumerate(records, 1):
            if not text:
                continue
                
            if idx % 100 == 0:
                print(f"Progress: {idx}/{total}")
                
            # Check for errors in this text
            text_errors = self.check_text(text, dict_words)
            
            for error in text_errors:
                errors.append(SpellingError(
                    id=record_id,
                    original_text=text,
                    keyword=error['keyword'],
                    full_word=error['full_word'],
                    error_type=error['error_type'],
                    suggestion=error['suggestion']
                ))
        
        return errors
    
    def print_errors(self, errors: List[SpellingError], limit: int = 50):
        """Print errors in a formatted way"""
        if not errors:
            print("No spelling errors found!")
            return
            
        print(f"\n{'='*80}")
        print(f"Found {len(errors)} spelling errors")
        print(f"{'='*80}\n")
        
        for i, error in enumerate(errors[:limit], 1):
            print(f"Error #{i}:")
            print(f"  ID: {error.id}")
            print(f"  Text: {error.original_text}")
            print(f"  Problem: '{error.keyword}' is embedded in '{error.full_word}'")
            print(f"  Suggestion: {error.suggestion}")
            print(f"  Type: {error.error_type}")
            print(f"{'-'*80}\n")
    
    def save_errors_to_json(self, errors: List[SpellingError], output_file: str = 'spelling_errors.json'):
        """Save errors to JSON file for later analysis"""
        data = []
        for error in errors:
            data.append({
                'id': error.id,
                'original_text': error.original_text,
                'keyword': error.keyword,
                'full_word': error.full_word,
                'error_type': error.error_type,
                'suggestion': error.suggestion
            })
        
        with open(output_file, 'w', encoding='utf-8') as f:
            json.dump(data, f, ensure_ascii=False, indent=2)
        
        print(f"Saved {len(errors)} errors to {output_file}")
    
    def update_database(self, errors: List[SpellingError], auto_fix: bool = False):
        """Update database with fixed translations"""
        if not auto_fix:
            print("Auto-fix is disabled. Use --fix flag to enable.")
            return
            
        cursor = self.conn.cursor()
        fixed_count = 0
        
        for error in errors:
            # Generate fixed text
            fixed_text = error.original_text.replace(error.full_word, error.suggestion)
            
            try:
                cursor.execute(
                    "UPDATE PoTranslations SET SuggestedTranslation = ? WHERE ID = ?",
                    (fixed_text, error.id)
                )
                fixed_count += 1
                if fixed_count % 10 == 0:
                    print(f"Fixed {fixed_count} records...")
            except Exception as e:
                print(f"Error fixing record {error.id}: {e}")
        
        self.conn.commit()
        print(f"Fixed {fixed_count} spelling errors in database")


def main():
    """Main function to run spell checking"""
    import argparse
    
    parser = argparse.ArgumentParser(description='Check Vietnamese spelling errors in database')
    parser.add_argument('--fix', action='store_true', help='Auto-fix detected errors')
    parser.add_argument('--limit', type=int, default=50, help='Limit number of errors to display')
    parser.add_argument('--output', type=str, default='spelling_errors.json', help='Output JSON file')
    args = parser.parse_args()
    
    # Initialize spell checker
    checker = VietnameseSpellChecker()
    
    # Get all errors
    print("Scanning database for errors...")
    errors = checker.get_errors_from_database()
    
    # Print results
    if errors:
        checker.print_errors(errors, limit=args.limit)
        checker.save_errors_to_json(errors, args.output)
        
        if args.fix:
            confirm = input("Do you want to auto-fix these errors? (y/n): ")
            if confirm.lower() == 'y':
                checker.update_database(errors, auto_fix=True)
    else:
        print("✅ No spelling errors found!")
    
    # Close database connection
    checker.conn.close()


if __name__ == "__main__":
    main()