local env = env

local main = mods.VietnameseLang
local AddPrefabPostInit = AddPrefabPostInit
local AddClassPostConstruct = env.AddClassPostConstruct
local modimport = env.modimport

GLOBAL.setfenv(1, GLOBAL)

local Levels = require("map/levels")

require("constants")

modimport('scripts/fix.lua')

-- Tải tệp ngôn ngữ
print("Đang tải tệp Việt hóa...")
env.LoadPOFile(main.StorePath..main.MainPoFile, main.SelectedLanguage)
main.PO = LanguageTranslator.languages[main.SelectedLanguage]

if not main.PO then
    print("[DST-Viet] LỖI: Không tải được " .. main.MainPoFile .. " — mod sẽ không dịch.")
    return
end

for k, v in pairs(main.PO) do
	if v == "<trống>" or v == "" or v:find("PLACEHOLDER", 1, true) then
		main.PO[k] = nil
	end
end

-- Áp dụng bản dịch vào bảng STRINGS ngay lập tức
-- (gọi sớm đảm bảo các mod khác không sử dụng lại STRINGS tiếng Anh cũ)
TranslateStringTable(STRINGS)
print("Đã tải tệp Việt hóa xong.")

local vi = main.PO 

-- Thay đổi tên các chế độ chơi
if rawget(_G, "GAME_MODES") and STRINGS.UI.GAMEMODES then
	for i,v in pairs(GAME_MODES) do
		for ii,vv in pairs(STRINGS.UI.GAMEMODES) do
			if v.text ~= nil and v.text == vv then
				GAME_MODES[i].text = main.PO["STRINGS.UI.GAMEMODES."..ii] or GAME_MODES[i].text
			end
			if v.description ~= nil and v.description == vv then
				GAME_MODES[i].description = main.PO["STRINGS.UI.GAMEMODES."..ii] or GAME_MODES[i].description
			end
		end
	end
end

-- Móc vào C++ TextWidget an toàn để dịch toàn bộ chữ động
-- Tối ưu: Dùng LRU Cache 512 slot để tránh tra cứu bảng 70k phần tử mỗi lần gọi.
-- TextWidget.SetString được gọi hàng nghìn lần/giây nên cần tránh tra cứu nặng lặp lại.
local _CACHE_MAX = 512
local _cache_lookup = {}   -- cache[str] = translated_str
local _cache_keys = {}     -- danh sách key theo thứ tự FIFO để evict
local _cache_count = 0

local oldSetString = _G.TextWidget.SetString
_G.TextWidget.SetString = function(guid, str)
    if type(str) == "string" and _G.VietnameseTextFixTable then
        local cached = _cache_lookup[str]
        if cached ~= nil then
            -- Cache hit: dùng kết quả đã tính sẵn
            str = cached
        else
            -- Cache miss: tra cứu bảng lớn, rồi lưu vào cache
            local translated = _G.VietnameseTextFixTable[str] or str
            if _cache_count >= _CACHE_MAX then
                -- Evict entry cũ nhất (FIFO) khi cache đầy
                local evict_key = _cache_keys[1]
                table.remove(_cache_keys, 1)
                _cache_lookup[evict_key] = nil
                _cache_count = _cache_count - 1
            end
            _cache_lookup[str] = translated
            _cache_keys[_cache_count + 1] = str
            _cache_count = _cache_count + 1
            str = translated
        end
    end
    oldSetString(guid, str)
end

