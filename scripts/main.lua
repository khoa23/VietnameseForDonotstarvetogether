local env = env

local main = mods.UkrainianLang
local AddPrefabPostInit = AddPrefabPostInit
local AddClassPostConstruct = env.AddClassPostConstruct
local modimport = env.modimport

GLOBAL.setfenv(1, GLOBAL)

local Levels = require("map/levels")

require("constants")

modimport('scripts/fix.lua')

-- Заванатаження файлу локалізації
print("Завантаження файлу локалізації")
env.LoadPOFile(main.StorePath..main.MainPoFile, main.SelectedLanguage)
main.PO = LanguageTranslator.languages[main.SelectedLanguage]

for k, v in pairs(main.PO) do
	if v == "<порожньо>" or v == "" or v:find("*PLACEHOLDER") then
		main.PO[k] = nil
	end
end
print("Файл заванатажено")

local ua = main.PO 

--Підміна назв режимів гри
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

