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

for k, v in pairs(main.PO) do
	if v == "<trống>" or v == "" or v:find("*PLACEHOLDER") then
		main.PO[k] = nil
	end
end
print("Đã tải tệp Việt hóa xong.")

local vi = main.PO 

-- Thay đổi tên các chế độ chơi
if rawget(_G, "GAME_MODES") and STRINGS.UI.GAMEMODES then
	for i,v in pairs(GAME_MODES) do
		for ii,vv in pairs(STRINGS.UI.GAMEMODES) do
			if v.text==vv then
				GAME_MODES[i].text = main.PO["STRINGS.UI.GAMEMODES."..ii] or GAME_MODES[i].text
			end
			if v.description==vv then
				GAME_MODES[i].description = main.PO["STRINGS.UI.GAMEMODES."..ii] or GAME_MODES[i].description
			end
		end
	end
end


