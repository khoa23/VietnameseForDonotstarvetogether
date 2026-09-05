import json
import re
from typing import Dict, Tuple, List, Optional
import logging
import numpy as np
from sklearn.metrics.pairwise import cosine_similarity
import torch
from transformers import AutoModel, AutoTokenizer
import py_vncorenlp

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

class TranslationSelector:
    def __init__(self, dictionary_path: str = 'dictionary.json', use_nlp: bool = True):
        """
        Khởi tạo Translation Selector với NLP
        """
        # Load dictionary
        with open(dictionary_path, 'r', encoding='utf-8') as f:
            self.dictionary = json.load(f)
        
        self.game_terms = self.dictionary.get('game_terms', {})
        self.typo_fixes = self.dictionary.get('typo_fixes', {})
        self.bad_translations = self.dictionary.get('bad_translations', [])
        
        # Tạo từ điển mapping
        self.term_mapping = {}
        for term, data in self.game_terms.items():
            for vn_word in data['vn']:
                self.term_mapping[vn_word.lower()] = term
        
        # Cache
        self._cache = {}
        self.use_nlp = use_nlp
        
        # Khởi tạo NLP components
        if self.use_nlp:
            try:
                logger.info("🔄 Đang khởi tạo NLP components...")
                
                # 1. VnCoreNLP cho tách từ
                logger.info("   ├─ Đang tải VnCoreNLP...")
                self.rdrsegmenter = py_vncorenlp.VnCoreNLP(
                    annotators=["wseg"],
                    save_dir='/absolute/path/to/vncorenlp'  # Thay bằng đường dẫn thực tế
                )
                logger.info("   │  └─ ✅ VnCoreNLP đã sẵn sàng")
                
                # 2. PhoBERT cho embedding
                logger.info("   ├─ Đang tải PhoBERT...")
                self.phobert = AutoModel.from_pretrained("vinai/phobert-base-v2")
                self.tokenizer = AutoTokenizer.from_pretrained("vinai/phobert-base-v2")
                logger.info("   │  └─ ✅ PhoBERT đã sẵn sàng")
                
                # Chuyển sang GPU nếu có
                if torch.cuda.is_available():
                    self.phobert = self.phobert.cuda()
                    logger.info("   └─ ✅ Đang sử dụng GPU")
                else:
                    logger.info("   └─ ⚠️ Đang sử dụng CPU (chậm hơn)")
                
            except Exception as e:
                logger.error(f"❌ Lỗi khởi tạo NLP: {e}")
                logger.info("⚠️ Fallback sang chế độ rule-based")
                self.use_nlp = False
        
        logger.info("✅ TranslationSelector đã khởi tạo")
    
    def select_translation(self, msgstr: str, suggested: str, msg_id: str = '') -> Tuple[str, float]:
        """
        Chọn bản dịch phù hợp và trả về rating
        """
        # Cache
        cache_key = f"{msgstr}|{suggested}"
        if cache_key in self._cache:
            return self._cache[cache_key]
        
        # =============================================
        # TRƯỜNG HỢP 1: KHÔNG CÓ SUGGESTED
        # =============================================
        if not suggested or suggested.strip() == '':
            if msg_id:
                logger.info(f"⚠️ {msg_id}: Không có SuggestedTranslation -> rating 0.5")
            result = (msgstr, 0.5)
            self._cache[cache_key] = result
            return result
        
        # =============================================
        # TRƯỜNG HỢP 2: GIỐNG HỆT
        # =============================================
        if msgstr and suggested and msgstr.strip() == suggested.strip():
            if msg_id:
                logger.info(f"⚖️ {msg_id}: MsgStr và Suggested giống hệt nhau -> 0.5")
            result = (msgstr, 0.5)
            self._cache[cache_key] = result
            return result
        
        # =============================================
        # TRƯỜNG HỢP 3: CÓ SUGGESTED
        # =============================================
        if not msgstr or msgstr.strip() == '':
            result = (suggested, 1.1)
            self._cache[cache_key] = result
            return result
        
        # =============================================
        # KIỂM TRA ĐỊNH DẠNG (Ưu tiên cao nhất)
        # =============================================
        format_score = self.compare_formatting(msgstr, suggested)
        
        # Nếu chênh lệch định dạng lớn
        if format_score > 0.7:
            if msg_id:
                logger.info(f"📐 {msg_id}: MsgStr giữ định dạng tốt hơn -> 1.1")
            result = (msgstr, 1.1)
            self._cache[cache_key] = result
            return result
        elif format_score < 0.3:
            if msg_id:
                logger.info(f"📐 {msg_id}: Suggested giữ định dạng tốt hơn -> 1.1")
            result = (suggested, 1.1)
            self._cache[cache_key] = result
            return result
        
        # =============================================
        # KIỂM TRA TƯƠNG ĐƯƠNG
        # =============================================
        if self.is_equivalent(msgstr, suggested):
            if msg_id:
                logger.info(f"⚖️ {msg_id}: Hai bản dịch tương đương -> 0.5")
            result = (msgstr, 0.5)
            self._cache[cache_key] = result
            return result
        
        # =============================================
        # KIỂM TRA LỖI CHÍNH TẢ
        # =============================================
        if self.has_typo_fix(msgstr, suggested):
            if msg_id:
                logger.info(f"🔧 {msg_id}: Sửa lỗi chính tả -> 1.1")
            result = (suggested, 1.1)
            self._cache[cache_key] = result
            return result
        
        # =============================================
        # KIỂM TRA DỊCH XẤU
        # =============================================
        if self.is_bad_translation(msgstr) and not self.is_bad_translation(suggested):
            if msg_id:
                logger.info(f"🚫 {msg_id}: MsgStr xấu -> dùng Suggested -> 1.1")
            result = (suggested, 1.1)
            self._cache[cache_key] = result
            return result
        
        if self.is_bad_translation(suggested) and not self.is_bad_translation(msgstr):
            if msg_id:
                logger.info(f"🚫 {msg_id}: Suggested xấu -> giữ MsgStr -> 1.1")
            result = (msgstr, 1.1)
            self._cache[cache_key] = result
            return result
        
        # =============================================
        # SO SÁNH NGỮ NGHĨA BẰNG NLP
        # =============================================
        semantic_score = self.compare_semantic(msgstr, suggested)
        naturalness_score = self.compare_naturalness_simple(msgstr, suggested)
        context_score = self.compare_context(msgstr, suggested)
        
        # Tổng hợp điểm (có trọng số)
        if self.use_nlp:
            # Ưu tiên NLP cho ngữ nghĩa
            total_score = (format_score * 0.2 + 
                          semantic_score * 0.4 + 
                          naturalness_score * 0.2 + 
                          context_score * 0.2)
        else:
            total_score = (format_score * 0.3 + 
                          naturalness_score * 0.35 + 
                          context_score * 0.35)
        
        # =============================================
        # QUYẾT ĐỊNH
        # =============================================
        if total_score >= 0.7:
            if msg_id:
                logger.info(f"✅ {msg_id}: Suggested tốt hơn (score={total_score:.2f}) -> 1.1")
            result = (suggested, 1.1)
        else:
            if msg_id:
                logger.info(f"❌ {msg_id}: Suggested không tốt hơn (score={total_score:.2f}) -> 0.5")
            result = (msgstr, 0.5)
        
        self._cache[cache_key] = result
        return result
    
    def compare_semantic(self, msgstr: str, suggested: str) -> float:
        """
        So sánh ngữ nghĩa bằng PhoBERT
        """
        if not self.use_nlp:
            return 0.5
        
        try:
            # =============================================
            # BƯỚC 1: Tách từ bằng VnCoreNLP
            # =============================================
            # Chỉ tách từ nếu là tiếng Việt
            if self.is_vietnamese(msgstr):
                msg_segmented = self.rdrsegmenter.word_segment(msgstr)
                sug_segmented = self.rdrsegmenter.word_segment(suggested)
            else:
                # Tiếng Anh không cần tách từ
                msg_segmented = msgstr
                sug_segmented = suggested
            
            # =============================================
            # BƯỚC 2: Tạo embedding bằng PhoBERT
            # =============================================
            # Tokenize và encode
            msg_tokens = self.tokenizer.encode(msg_segmented, return_tensors='pt')
            sug_tokens = self.tokenizer.encode(sug_segmented, return_tensors='pt')
            
            # Chuyển sang GPU nếu có
            if torch.cuda.is_available():
                msg_tokens = msg_tokens.cuda()
                sug_tokens = sug_tokens.cuda()
            
            # Tạo embedding
            with torch.no_grad():
                msg_embedding = self.phobert(msg_tokens).last_hidden_state[:, 0, :]
                sug_embedding = self.phobert(sug_tokens).last_hidden_state[:, 0, :]
            
            # Chuyển về CPU để tính cosine
            msg_embedding = msg_embedding.cpu().numpy()
            sug_embedding = sug_embedding.cpu().numpy()
            
            # =============================================
            # BƯỚC 3: Tính độ tương đồng cosine
            # =============================================
            similarity = cosine_similarity(msg_embedding, sug_embedding)[0][0]
            
            # =============================================
            # BƯỚC 4: Chuyển đổi thành điểm
            # =============================================
            # similarity: 0-1
            # Nếu similarity > 0.9 → giống nghĩa → 0.5 (tương đương)
            # Nếu similarity < 0.5 → khác nghĩa → cần đánh giá thêm
            if similarity > 0.9:
                return 0.5  # Giống nghĩa → tương đương
            elif similarity < 0.5:
                return 0.6  # Khác nghĩa → có thể tốt hơn
            else:
                # Middle range
                return 0.5 + (similarity - 0.5) * 0.5
            
        except Exception as e:
            logger.warning(f"⚠️ NLP error: {e}, fallback to 0.5")
            return 0.5
    
    def is_vietnamese(self, text: str) -> bool:
        """
        Kiểm tra văn bản có phải tiếng Việt không
        Dựa trên số lượng ký tự có dấu
        """
        if not text:
            return False
        
        vietnamese_ratio = self.vietnamese_char_ratio(text)
        return vietnamese_ratio > 0.1
    
    def compare_formatting(self, msgstr: str, suggested: str) -> float:
        """
        So sánh khả năng giữ định dạng đặc biệt
        """
        special_chars = {
            'escape': r'\\',
            'quote': r'\"',
            'double_quote': r'"',
            'single_quote': r"'",
            'bracket': r'[\[\](){}]',
            'punctuation': r'[.,!?;:]',
            'special': r'[#$%&@^~]',
        }
        
        msg_score = 0
        sug_score = 0
        total_weight = 0
        
        for name, pattern in special_chars.items():
            msg_count = len(re.findall(pattern, msgstr))
            sug_count = len(re.findall(pattern, suggested))
            
            weight = 2.0 if name in ['escape', 'quote'] else 1.0
            
            if msg_count > 0 or sug_count > 0:
                total_weight += weight
                if msg_count >= sug_count:
                    msg_score += weight
                else:
                    sug_score += weight
        
        if total_weight == 0:
            return 0.5
        
        msg_ratio = msg_score / total_weight
        sug_ratio = sug_score / total_weight
        
        if msg_ratio > sug_ratio:
            return 0.7 + (msg_ratio - sug_ratio) * 0.3
        elif sug_ratio > msg_ratio:
            return 0.3 + (sug_ratio - msg_ratio) * 0.3
        else:
            return 0.5
    
    def is_equivalent(self, text1: str, text2: str) -> bool:
        """
        Kiểm tra hai bản dịch có tương đương không
        """
        if text1.strip() == text2.strip():
            return True
        
        len1 = len(text1)
        len2 = len(text2)
        if abs(len1 - len2) / max(len1, len2) > 0.2:
            return False
        
        words1 = set(text1.lower().split())
        words2 = set(text2.lower().split())
        
        if len(words1) == 0 or len(words2) == 0:
            return False
        
        common = words1 & words2
        union = words1 | words2
        
        similarity = len(common) / len(union)
        
        if similarity > 0.7:
            return True
        
        # Từ điển đồng nghĩa
        synonyms = {
            'không': ['đừng', 'chẳng', 'chả'],
            'ngừng': ['dừng', 'thôi'],
            'học': ['học tập'],
            'hỏi': ['học hỏi', 'tìm hiểu'],
        }
        
        def normalize(word):
            for key, syns in synonyms.items():
                if word in syns:
                    return key
            return word
        
        words1_norm = {normalize(w) for w in words1}
        words2_norm = {normalize(w) for w in words2}
        
        common_norm = words1_norm & words2_norm
        union_norm = words1_norm | words2_norm
        
        if len(union_norm) == 0:
            return False
        
        similarity_norm = len(common_norm) / len(union_norm)
        
        return similarity_norm > 0.7
    
    def has_typo_fix(self, msgstr: str, suggested: str) -> bool:
        """Kiểm tra lỗi chính tả"""
        for typo, correct in self.typo_fixes.items():
            if typo in msgstr.lower() and correct in suggested.lower():
                return True
        return False
    
    def is_bad_translation(self, text: str) -> bool:
        """Kiểm tra bản dịch xấu"""
        text_lower = text.lower()
        for bad in self.bad_translations:
            if bad in text_lower:
                return True
        
        bad_patterns = [
            r'^[a-z]+$',
            r'^[0-9]+$',
            r'^[a-z0-9]+$',
            r'^[a-z\s]+$',
        ]
        for pattern in bad_patterns:
            if re.match(pattern, text_lower):
                return True
        
        return False
    
    def compare_naturalness_simple(self, msgstr: str, suggested: str) -> float:
        """So sánh độ tự nhiên"""
        msg_words = msgstr.split()
        sug_words = suggested.split()
        
        if len(msg_words) == 0 or len(sug_words) == 0:
            return 0.5
        
        msg_unique = len(set(msg_words))
        sug_unique = len(set(sug_words))
        
        msg_vietnamese_ratio = self.vietnamese_char_ratio(msgstr)
        sug_vietnamese_ratio = self.vietnamese_char_ratio(suggested)
        
        score = 0.5
        
        if len(sug_words) > len(msg_words):
            score += 0.2
        elif len(sug_words) < len(msg_words):
            score -= 0.2
        
        if sug_vietnamese_ratio > msg_vietnamese_ratio:
            score += 0.2
        elif sug_vietnamese_ratio < msg_vietnamese_ratio:
            score -= 0.2
        
        if sug_unique > msg_unique:
            score += 0.1
        
        return max(0, min(1, score))
    
    def vietnamese_char_ratio(self, text: str) -> float:
        """Tính tỉ lệ ký tự có dấu tiếng Việt"""
        vietnamese_chars = set('áàảãạăắằẳẵặâấầẩẫậéèẻẽẹêếềểễệíìỉĩịóòỏõọôốồổỗộơớờởỡợúùủũụưứừửữựýỳỷỹỵđ')
        total_chars = len(text)
        if total_chars == 0:
            return 0
        vietnamese_count = sum(1 for c in text if c in vietnamese_chars)
        return vietnamese_count / total_chars
    
    def compare_context(self, msgstr: str, suggested: str) -> float:
        """So sánh ngữ cảnh game"""
        game_keywords = ['abandon', 'ship', 'merm', 'spelunk', 'ransack', 
                        'investigate', 'walrus', 'beast', 'turkey', 'clockwork',
                        'abigail', 'deerclops', 'bearger', 'dragonfly', 'moose',
                        'klaus', 'toadstool', 'bee', 'queen', 'shadow']
        
        msg_score = 0
        sug_score = 0
        
        for keyword in game_keywords:
            if keyword in msgstr.lower():
                msg_score += 1
            if keyword in suggested.lower():
                sug_score += 1
        
        if len(suggested) > len(msgstr) * 1.3:
            sug_score -= 0.5
        elif len(suggested) < len(msgstr) * 0.7:
            sug_score -= 0.5
        
        for word in msgstr.lower().split():
            if word in self.term_mapping:
                msg_score += 0.5
        for word in suggested.lower().split():
            if word in self.term_mapping:
                sug_score += 0.5
        
        if sug_score > msg_score:
            return 0.8
        elif sug_score == msg_score:
            return 0.5
        else:
            return 0.3
    
    def clear_cache(self):
        """Xóa cache"""
        self._cache.clear()