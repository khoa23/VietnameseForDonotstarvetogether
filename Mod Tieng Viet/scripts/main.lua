local env = env

local main = mods.VietnameseLang

GLOBAL.setfenv(1, GLOBAL)

-- ============================================================================
-- VÌ SAO TRƯỚC ĐÂY CẦN fix.lua?
--
-- Trong main.lua của game, thứ tự khởi động là:
--   1. require("strings") + TranslateStringTable(STRINGS)   (chỉ có ngôn ngữ chính thức)
--   2. require hàng loạt module: map/levels, map/customize, map/tasksets,
--      map/startlocations, recipes, ...  -> các module này COPY giá trị chuỗi
--      tiếng Anh vào bảng riêng của chúng (name = STRINGS..., text = STRINGS...)
--   3. ModManager:LoadMods()  -> lúc này mod mới được chạy
--
-- Mod dịch bảng STRINGS ở bước 3 thì các bản copy ở bước 2 vẫn là tiếng Anh
-- (tên/mô tả preset thế giới, các lựa chọn "Never/Rare/Default...", "Long Day",
-- tên điểm xuất phát, bộ lọc trạm chế tạo...). Chính game cũng ghi chú vấn đề
-- này ở main.lua: "it doesn't address the issue for mod translations".
--
-- Cách khắc phục: khi dịch STRINGS, ghi lại cặp (tiếng Anh -> tiếng Việt) lấy
-- trực tiếp từ file .po, rồi dùng nó để dịch lại các bảng đã bị copy sớm.
-- Không cần file fix.lua 7MB chép lại toàn bộ .po nữa.
-- ============================================================================

-- Đọc .po nhanh hơn Translator:LoadPOFile của game: hàm gốc nạp TOÀN BỘ
-- ~450k dòng vào một mảng tạm rồi mới duyệt, và chạy 4 lần gsub cho mỗi chuỗi.
-- Ở đây đọc từng dòng trực tiếp, chỉ gsub khi chuỗi có ký tự '\'.
-- Kết quả giống hệt hàm gốc (định dạng msgctxt, bỏ qua msgstr rỗng).
local function FastLoadPOFile(fname, lang)
	require("translator")
	local path = resolvefilepath(fname)
	local file = path ~= nil and io.open(path, "r") or nil
	if file == nil then return false end

	local strings = {}
	local id, str = nil, nil
	local function flush()
		if id ~= nil and str ~= nil and str ~= "" then
			if str:find("\\", 1, true) then
				str = LanguageTranslator:ConvertEscapeCharactersToRaw(str)
			end
			strings[id] = str
		end
		id, str = nil, nil
	end

	for line in file:lines() do
		local b = line:byte(1)
		if b == 34 then -- '"': dòng nối tiếp của msgstr nhiều dòng
			if str ~= nil then
				local s = line:match('^"(.*)"')
				if s ~= nil then str = str..s end
			end
		elseif b == 109 then -- 'm': msgctxt / msgid / msgstr
			if line:find("^msgctxt") then
				flush()
				id = line:match('^msgctxt%s*"(%S*)"')
			elseif id ~= nil and str == nil and line:find("^msgstr") then
				str = line:match('^msgstr%s*"(.*)"')
			end
		elseif str ~= nil then
			flush()
		end
	end
	flush()
	file:close()

	LanguageTranslator.languages[lang] = strings
	LanguageTranslator.defaultlang = lang
	return true
end

-- Tải tệp ngôn ngữ
print("[DST-Viet] Đang tải tệp Việt hóa...")
local ok, loaded = pcall(FastLoadPOFile, main.StorePath..main.MainPoFile, main.SelectedLanguage)
if not (ok and loaded) then
	-- Dự phòng: dùng hàm đọc gốc của game
	env.LoadPOFile(main.StorePath..main.MainPoFile, main.SelectedLanguage)
end
main.PO = LanguageTranslator.languages[main.SelectedLanguage]

if not main.PO then
	print("[DST-Viet] LỖI: Không tải được " .. main.MainPoFile .. " — mod sẽ không dịch.")
	return
end

local PO = main.PO
for k, v in pairs(PO) do
	if v == "<trống>" or v == "" or v:find("PLACEHOLDER", 1, true) then
		PO[k] = nil
	end
end

-- Bảng tra ngược: chuỗi tiếng Anh -> chuỗi tiếng Việt
local en2vi = {}

-- Giữ lại vài chuỗi tiếng Anh gốc cần cho các chuỗi ghép (xem COMPOSED bên dưới)
local SANDBOX_EN = {}
local SANDBOX = STRINGS.UI and STRINGS.UI.SANDBOXMENU
if SANDBOX then
	for _, key in ipairs({ "SLIDELONG", "EXCLUDE", "SLIDEALL", "DAY", "DUSK", "NIGHT" }) do
		SANDBOX_EN[key] = SANDBOX[key]
	end
end

-- Bản tiếng Anh của STRINGS.UI.HUD và STRINGS.NAMES, dùng để dịch thông báo
-- chết/hồi sinh do server gửi xuống (xem phần cuối file)
local HUD_EN, NAMES_EN = {}, {}
for k, v in pairs(STRINGS.UI.HUD) do
	if type(v) == "string" then HUD_EN[k] = v end
end
for k, v in pairs(STRINGS.NAMES) do
	if type(v) == "string" then NAMES_EN[k] = v end
end

-- Các bảng chứa câu thông báo mà server hay gửi qua TheNet:Announce
-- (STRINGS.EYEOFTERROR_COMING, STRINGS.TWINS_TARGET, UI.HUD.RETROFITTING_ANNOUNCEMENT...)
-- Lưu {bảng, key, tiếng Anh}; sau khi dịch, bảng[key] chính là tiếng Việt.
local ANNOUNCE_EN = {}
for _, tbl in ipairs({ STRINGS, STRINGS.UI.HUD, STRINGS.UI.NOTIFICATION }) do
	if type(tbl) == "table" then
		for k, v in pairs(tbl) do
			if type(v) == "string" then
				ANNOUNCE_EN[#ANNOUNCE_EN + 1] = { tbl, k, v }
			end
		end
	end
end

-- Dịch STRINGS và xây bảng tra ngược trong CÙNG MỘT lần duyệt.
-- Khi một câu tiếng Anh có nhiều bản dịch, ưu tiên bản dịch thuộc STRINGS.UI
-- (các bảng bị copy sớm hầu hết là chuỗi giao diện).
local function TranslateAndIndex(path, tbl, is_ui)
	for k, v in pairs(tbl) do
		local key = path.."."..k
		local tv = type(v)
		if tv == "table" then
			TranslateAndIndex(key, v, is_ui)
		elseif tv == "string" then
			local vi = PO[key]
			if vi ~= nil then
				-- Đã dịch xong thì bỏ khỏi PO: sau khi load mod, game gọi lại
				-- TranslateStringTable(STRINGS) (main.lua: ModSafeStartup), nhờ vậy
				-- lần dịch đó không phải làm lại 87k mục, đồng thời giải phóng bộ nhớ.
				-- Mục còn lại (key của mod khác chưa có lúc này) vẫn được game dịch.
				PO[key] = nil
				-- Giống Translator:GetTranslatedString (chỉ cần khi có ký tự '\')
				if vi:find("\\", 1, true) then
					vi = LanguageTranslator:ConvertEscapeCharactersToRaw(vi)
				end
				if vi ~= v then
					tbl[k] = vi
					if is_ui or en2vi[v] == nil then
						en2vi[v] = vi
					end
				end
			end
		end
	end
end

-- Chuỗi nằm ngay ở cấp STRINGS.* (vd STRINGS.EYEOFTERROR_COMING) cũng phải
-- được dịch + đưa vào bảng tra ngược, không chỉ các bảng con.
local root_strings = {}
for k, v in pairs(STRINGS) do
	if type(v) == "table" then
		TranslateAndIndex("STRINGS."..k, v, k == "UI")
	elseif type(v) == "string" then
		root_strings[k] = v
	end
end
TranslateAndIndex("STRINGS", root_strings, false)
for k, v in pairs(root_strings) do
	STRINGS[k] = v
end
root_strings = nil

-- Các chuỗi được game ghép lúc khởi động (map/customize.lua: SLIDELONG.." "..DAY ...)
-- không tồn tại nguyên văn trong STRINGS nên phải khai báo riêng.
local COMPOSED = {
	{ "SLIDELONG", "DAY",   "Ngày dài" },
	{ "SLIDELONG", "DUSK",  "Mờ tối dài" },
	{ "SLIDELONG", "NIGHT", "Đêm dài" },
	{ "EXCLUDE",   "DAY",   "Không ngày" },
	{ "EXCLUDE",   "DUSK",  "Không mờ tối" },
	{ "EXCLUDE",   "NIGHT", "Không đêm" },
	{ "SLIDEALL",  "DAY",   "Chỉ ngày" },
	{ "SLIDEALL",  "DUSK",  "Chỉ mờ tối" },
	{ "SLIDEALL",  "NIGHT", "Chỉ đêm" },
}
for _, c in ipairs(COMPOSED) do
	local a, b = SANDBOX_EN[c[1]], SANDBOX_EN[c[2]]
	if type(a) == "string" and type(b) == "string" then
		en2vi[a.." "..b] = c[3]
	end
end

print("[DST-Viet] Đã tải tệp Việt hóa xong.")

-- ============================================================================
-- Dịch lại các bảng đã copy chuỗi tiếng Anh trước khi mod được load
-- ============================================================================
local TEXT_FIELDS = {
	text = true, name = true, desc = true, description = true,
	label = true, hover = true, title = true, filter_text = true,
	-- map/level.lua & map/settings.lua copy name/desc sang các trường này,
	-- màn hình chọn preset/playstyle hiển thị trực tiếp settings_desc
	worldgen_name = true, worldgen_desc = true,
	settings_name = true, settings_desc = true,
}

-- Không bao giờ đi vào các bảng lớn/chung này
local visited = { [STRINGS] = true, [_G] = true, [package.loaded] = true }

local function FixTable(t)
	if visited[t] then return end
	visited[t] = true
	for k, v in pairs(t) do
		local tv = type(v)
		if tv == "string" then
			-- Bỏ qua chuỗi dạng ID nội bộ ("forest", "default", "spiders"...)
			if TEXT_FIELDS[k] and not v:find("^[%l%d_]*$") then
				local vi = en2vi[v]
				if vi ~= nil then
					t[k] = vi
				end
			end
		elseif tv == "table" then
			FixTable(v)
		end
	end
end

-- Dữ liệu của các module map/* nằm trong biến local (upvalue) của các hàm
-- mà module trả về, nên phải duyệt qua upvalue để với tới.
local getupvalue = debug and debug.getupvalue
local function FixModule(modname)
	local m = package.loaded[modname]
	if type(m) ~= "table" then return end
	if getupvalue ~= nil then
		for _, fn in pairs(m) do
			if type(fn) == "function" then
				local i = 1
				while true do
					local name, val = getupvalue(fn, i)
					if name == nil then break end
					if type(val) == "table" then
						FixTable(val)
					end
					i = i + 1
				end
			end
		end
	end
	FixTable(m)
end

FixModule("map/levels")        -- tên/mô tả preset thế giới, playstyle
FixModule("map/tasksets")      -- tên bộ nhiệm vụ (Mặc định, Cổ điển...)
FixModule("map/startlocations")-- tên điểm xuất phát
FixModule("map/customize")     -- các lựa chọn tùy chỉnh thế giới
if rawget(_G, "PROTOTYPER_DEFS") then FixTable(PROTOTYPER_DEFS) end  -- bộ lọc trạm chế tạo
if rawget(_G, "GAME_MODES") then FixTable(GAME_MODES) end

visited = nil

-- ============================================================================
-- Dịch dự phòng: bắt các chuỗi tiếng Anh còn sót (do mod khác hoặc module
-- khác copy sớm) ngay khi hiển thị. Chỉ là 1 phép tra bảng băm O(1), không
-- cần cache. Có thể tắt trong cấu hình mod để tiết kiệm bộ nhớ.
-- ============================================================================
local TextWidget = rawget(_G, "TextWidget")
if main.FallbackTranslate and TextWidget ~= nil and TextWidget.SetString ~= nil then
	local oldSetString = TextWidget.SetString
	TextWidget.SetString = function(self, str, ...)
		if type(str) == "string" then
			str = en2vi[str] or str
		end
		return oldSetString(self, str, ...)
	end

	-- Các hàm cắt chữ của widget Text chia chuỗi gốc thành nhiều đoạn rồi mới
	-- gọi SetString, nên phải dịch NGAY ở đầu vào (trước khi bị cắt).
	local Text = require("widgets/text")
	for _, fname in ipairs({ "SetMultilineTruncatedString", "SetTruncatedString" }) do
		local old = Text[fname]
		if old ~= nil then
			Text[fname] = function(self, str, ...)
				if type(str) == "string" then
					str = en2vi[str] or str
				end
				return old(self, str, ...)
			end
		end
	end
else
	en2vi = nil -- giải phóng bộ nhớ
end

-- ============================================================================
-- Thông báo chết / hồi sinh
--
-- Câu "X was killed by Y. They became a spooky ghost!" được GHÉP SẴN TRÊN
-- SERVER (GetNewDeathAnnouncementString) rồi gửi xuống client qua
-- TheNet:AnnounceDeath. Mod này chỉ chạy ở client nên server (dedicated server,
-- server người khác, shard Hang động) vẫn dùng tiếng Anh. Ở đây ta tách câu
-- tiếng Anh nhận được thành từng phần đã biết rồi ghép lại bằng tiếng Việt.
-- ============================================================================
local HUD = STRINGS.UI.HUD

-- Tên vật thể/sinh vật: tiếng Anh -> tiếng Việt
local names_en2vi = {}
local name_template_pairs = {} -- mẫu tên có tham số: "{item} Sketch", "{item} Blueprint"...
local BLUEPRINT_EN = NAMES_EN.BLUEPRINT
for k, en in pairs(NAMES_EN) do
	local vi = STRINGS.NAMES[k]
	if type(vi) == "string" and vi ~= en then
		names_en2vi[en] = vi
		if en:find("{[%w_]+}") then
			name_template_pairs[#name_template_pairs + 1] = { en, vi }
		end
	end
end
NAMES_EN = nil

-- Các đuôi câu theo giới tính (". He became a spooky ghost!", "lost all of ...")
local tails = {}
for k, en in pairs(HUD_EN) do
	if (k:find("^DEATH_ANNOUNCEMENT_2_") or k:find("^GHOST_DEATH_ANNOUNCEMENT_"))
		and type(HUD[k]) == "string" and HUD[k] ~= en then
		tails[#tails + 1] = { en, HUD[k] }
	end
end
table.sort(tails, function(a, b) return #a[1] > #b[1] end)

local function EndsWith(s, suffix)
	return #s >= #suffix and s:sub(-#suffix) == suffix
end

local function TranslateSource(src)
	return names_en2vi[src] or src
end

local function TranslateDeathMessage(msg)
	local body, tail = msg, nil
	for _, t in ipairs(tails) do
		if EndsWith(msg, t[1]) then
			body, tail = msg:sub(1, -#t[1] - 1), t[2]
			break
		end
	end
	if tail == nil and EndsWith(msg, ".") then
		body, tail = msg:sub(1, -2), "."
	end

	local killed_en = HUD_EN.DEATH_ANNOUNCEMENT_1
	if killed_en ~= nil then
		local i, j = body:find(" "..killed_en.." ", 1, true)
		if i ~= nil then
			return body:sub(1, i)..HUD.DEATH_ANNOUNCEMENT_1.." "..TranslateSource(body:sub(j + 1))..(tail or "")
		end
	end
	-- "X lost all of their humanity and is gone forever."
	return tail ~= nil and tail ~= "." and body..tail or msg
end

local function TranslateRezMessage(msg)
	local rez_en = HUD_EN.REZ_ANNOUNCEMENT
	if rez_en == nil then return msg end
	local i, j = msg:find(" "..rez_en.." ", 1, true)
	if i == nil then return msg end
	local src = msg:sub(j + 1)
	local dot = EndsWith(src, ".") and "." or ""
	if dot ~= "" then src = src:sub(1, -2) end
	return msg:sub(1, i)..HUD.REZ_ANNOUNCEMENT.." "..TranslateSource(src)..dot
end

local function WrapAnnouncement(fname, translate)
	local old = rawget(_G, fname)
	if old == nil then return end
	_G[fname] = function(message, ...)
		if type(message) == "string" then
			message = translate(message)
		end
		return old(message, ...)
	end
end

WrapAnnouncement("Networking_DeathAnnouncement", TranslateDeathMessage)
WrapAnnouncement("Networking_ResurrectAnnouncement", TranslateRezMessage)

local MAGIC = "[%^%$%(%)%%%.%[%]%*%+%-%?]"

-- Biến câu mẫu tiếng Anh ("{item} Sketch", "... in %s seconds") thành pattern Lua
local function AddTemplate(list, en, vi)
	local names, positional = {}, false
	local tmp
	if en:find("{[%w_]+}") then
		tmp = en:gsub("{([%w_]+)}", function(n) names[#names + 1] = n; return "\1" end)
	elseif en:find("%%[sd]") then
		positional = true
		tmp = en:gsub("%%[sd]", "\1")
	else
		return
	end
	-- Bỏ mẫu gần như chỉ toàn tham số (vd "%s") vì sẽ khớp với mọi câu
	if #tmp:gsub("[%s%p\1]", "") < 4 then
		return
	end
	local pat = "^"..tmp:gsub(MAGIC, "%%%0"):gsub("\1", "(.-)").."$"
	-- Trùng mẫu tiếng Anh thì giữ mẫu được thêm trước
	for _, t in ipairs(list) do
		if t[1] == pat then return end
	end
	list[#list + 1] = { pat, vi, names, positional }
end

local function SortTemplates(list)
	-- Mẫu dài trước để khớp chính xác hơn
	table.sort(list, function(a, b) return #a[1] > #b[1] end)
end

-- Khớp msg với danh sách mẫu; capfn (nếu có) dịch từng giá trị tham số
local function ApplyTemplates(list, msg, capfn)
	for _, t in ipairs(list) do
		local caps = { msg:match(t[1]) }
		if caps[1] ~= nil then
			if capfn ~= nil then
				for i = 1, #caps do caps[i] = capfn(caps[i]) end
			end
			if t[4] then
				local i = 0
				return (t[2]:gsub("%%[sd]", function()
					i = i + 1
					return caps[i] or ""
				end))
			end
			local values = {}
			for idx, name in ipairs(t[3]) do
				values[name] = caps[idx]
			end
			return (t[2]:gsub("{([%w_]+)}", function(n) return values[n] or "" end))
		end
	end
	return nil
end

-- ============================================================================
-- Thông báo chung từ server (TheNet:Announce / TheNet:SystemMessage)
-- Ví dụ: "You feel an evil presence watching you..." (prefabs/terrarium.lua)
-- Cùng nguyên nhân như trên: server gửi câu tiếng Anh đã ghép sẵn.
-- ============================================================================
local announce_exact = {}     -- câu cố định: tiếng Anh -> tiếng Việt
local announce_templates = {} -- câu có tham số {name} hoặc %s

for _, e in ipairs(ANNOUNCE_EN) do
	local en, vi = e[3], e[1][e[2]]
	if type(vi) == "string" and vi ~= en then
		announce_exact[en] = vi
		AddTemplate(announce_templates, en, vi)
	end
end
ANNOUNCE_EN = nil
SortTemplates(announce_templates)

local function TranslateAnnouncement(msg)
	return announce_exact[msg]
		or (en2vi ~= nil and en2vi[msg])
		or ApplyTemplates(announce_templates, msg)
		or msg
end

WrapAnnouncement("Networking_Announcement", TranslateAnnouncement)
WrapAnnouncement("Networking_SystemMessage", TranslateAnnouncement)

-- ============================================================================
-- Tên đồ vật được server ghép rồi gửi qua component "named"
-- Ví dụ: "Scaled Furnace Blueprint" (prefabs/blueprint.lua:
--   STRINGS.NAMES[X].." "..STRINGS.NAMES.BLUEPRINT), "Dragonfly Figure Sketch"
--   (prefabs/sketch.lua: subfmt(STRINGS.NAMES.SKETCH, {item = ...})).
-- Tên này đi qua net_string tới client và còn được LƯU VÀO FILE SAVE, nên dù
-- server có dịch thì đồ đã có từ trước vẫn mang tên tiếng Anh.
-- Chỉ đổi inst.name ở máy mình, không đổi giá trị gửi qua mạng.
-- ============================================================================
local name_templates = {}
-- Dạng ghép "X Blueprint" (không dùng subfmt) -> "Bản thiết kế X".
-- Thêm TRƯỚC vì trùng nguyên văn với BLUEPRINT_RARE "{item} Blueprint".
if type(BLUEPRINT_EN) == "string" and type(STRINGS.NAMES.BLUEPRINT) == "string" then
	AddTemplate(name_templates, "{item} "..BLUEPRINT_EN, STRINGS.NAMES.BLUEPRINT.." {item}")
end
BLUEPRINT_EN = nil
for _, p in ipairs(name_template_pairs) do
	AddTemplate(name_templates, p[1], p[2])
end
name_template_pairs = nil
SortTemplates(name_templates)

local function TranslateName(name)
	if type(name) ~= "string" or name == "" then return name end
	return names_en2vi[name] or ApplyTemplates(name_templates, name, TranslateSource) or name
end

-- Client: tên nhận từ server
env.AddClassPostConstruct("components/named_replica", function(self, inst)
	if not TheWorld.ismastersim then
		inst:ListenForEvent("namedirty", function(inst)
			inst.name = TranslateName(inst.name)
		end)
	end
end)

-- Host (tự mở server): tên đặt trên máy mình hoặc nạp từ file save
env.AddComponentPostInit("named", function(self)
	local old_SetName = self.SetName
	self.SetName = function(self, ...)
		old_SetName(self, ...)
		self.inst.name = TranslateName(self.inst.name)
	end
	local old_OnLoad = self.OnLoad
	self.OnLoad = function(self, ...)
		old_OnLoad(self, ...)
		self.inst.name = TranslateName(self.inst.name)
	end
	local old_PickNewName = self.PickNewName
	self.PickNewName = function(self, ...)
		old_PickNewName(self, ...)
		self.inst.name = TranslateName(self.inst.name)
	end
end)
