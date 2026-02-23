_G = GLOBAL

-- Setup environment for the translation mod
mods = _G.rawget(_G, "mods")
if not mods then
    mods = {}
    _G.rawset(_G, "mods", mods)
end

-- Cấu hình thông tin mod Việt hóa
mods.VietnameseLang = {
    modinfo = modinfo,
    StorePath = MODROOT,
    MainPoFile = "vietnamese.po",
    SelectedLanguage = "vi"
}

-- Load the main scripts
modimport("scripts/main.lua")

-- The rest of the setup is handled in scripts/main.lua and scripts/fix.lua
