local env = env

local main = mods.VietnameseLang
local AddPrefabPostInit = AddPrefabPostInit
local AddClassPostConstruct = env.AddClassPostConstruct
local modimport = env.modimport

GLOBAL.setfenv(1, GLOBAL)

local Levels = require("map/levels")

require("constants")

modimport('scripts/fix.lua')

-- Hàm đọc file .mo (Gettext Binary MO Parser - Fast Memory Version)
local function LoadMOFile(filepath, lang_id)
    local f = io.open(filepath, "rb")
    if not f then
        return false
    end

    local data = f:read("*a")
    f:close()

    if not data or #data < 28 then
        return false
    end

    local function get_uint32_le(str, pos)
        local b1, b2, b3, b4 = string.byte(str, pos, pos + 3)
        if not (b1 and b2 and b3 and b4) then return nil end
        return b1 + b2 * 256 + b3 * 65536 + b4 * 16777216
    end

    local function get_uint32_be(str, pos)
        local b1, b2, b3, b4 = string.byte(str, pos, pos + 3)
        if not (b1 and b2 and b3 and b4) then return nil end
        return b4 + b3 * 256 + b2 * 65536 + b1 * 16777216
    end

    local magic = get_uint32_le(data, 1)
    local get_uint32 = get_uint32_le

    if magic == 0x950412de then
        get_uint32 = get_uint32_le
    elseif magic == 0xde120495 then
        get_uint32 = get_uint32_be
    else
        print("[DST-Viet] LỖI: File .mo không đúng định dạng magic number!")
        return false
    end

    local num_strings = get_uint32(data, 9)
    local orig_table_offset = get_uint32(data, 13)
    local trans_table_offset = get_uint32(data, 17)

    if not (num_strings and orig_table_offset and trans_table_offset) then
        return false
    end

    if not LanguageTranslator.languages[lang_id] then
        LanguageTranslator.languages[lang_id] = {}
    end
    local lang_tbl = LanguageTranslator.languages[lang_id]

    local o_pos = orig_table_offset + 1
    local t_pos = trans_table_offset + 1

    for i = 1, num_strings do
        local o_len = get_uint32(data, o_pos)
        local o_off = get_uint32(data, o_pos + 4)
        local t_len = get_uint32(data, t_pos)
        local t_off = get_uint32(data, t_pos + 4)

        if o_len and o_len > 0 and t_len and t_len > 0 then
            local msgid = string.sub(data, o_off + 1, o_off + o_len)
            local msgstr = string.sub(data, t_off + 1, t_off + t_len)
            lang_tbl[msgid] = msgstr
        end

        o_pos = o_pos + 8
        t_pos = t_pos + 8
    end

    return true
end

-- Tải tệp ngôn ngữ
print("Đang tải tệp Việt hóa (.mo binary)...")
local success = LoadMOFile(main.StorePath..main.MainPoFile, main.SelectedLanguage)

main.PO = LanguageTranslator.languages[main.SelectedLanguage]

if not success or not main.PO then
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
local oldSetString = _G.TextWidget.SetString
_G.TextWidget.SetString = function(guid, str)
    if type(str) == "string" and _G.VietnameseTextFixTable then
        str = _G.VietnameseTextFixTable[str] or str
    end
    oldSetString(guid, str)
end

