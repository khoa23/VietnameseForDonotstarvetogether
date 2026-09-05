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
    MainPoFile = "vietnamese.mo",
    SelectedLanguage = "vi"
}

-- Load the main scripts
modimport("scripts/main.lua")

-- The rest of the setup is handled in scripts/main.lua and scripts/fix.lua

-- Cấu hình Phông chữ (Font Setup)
local FONT_OPTION = GetModConfigData("FONT_OPTION")

if FONT_OPTION ~= "off" and FONT_OPTION ~= nil then
    local font_name = "nowar_normal"
    local font_name_outline = "nowar_normal_outline"
    
    local FontNames = {
        DEFAULTFONT = _G.DEFAULTFONT,
        DIALOGFONT = _G.DIALOGFONT,
        TITLEFONT = _G.TITLEFONT,
        UIFONT = _G.UIFONT,
        BUTTONFONT = _G.BUTTONFONT,
        NEWFONT = _G.NEWFONT,
        NEWFONT_SMALL = _G.NEWFONT_SMALL,
        NEWFONT_OUTLINE = _G.NEWFONT_OUTLINE,
        NEWFONT_OUTLINE_SMALL = _G.NEWFONT_OUTLINE_SMALL,
        NUMBERFONT = _G.NUMBERFONT,
        TALKINGFONT = _G.TALKINGFONT,
        SMALLNUMBERFONT = _G.SMALLNUMBERFONT,
        BODYTEXTFONT = _G.BODYTEXTFONT,
        CODEFONT = _G.CODEFONT,
        TALKINGFONT_WORMWOOD = _G.TALKINGFONT_WORMWOOD,
        CHATFONT = _G.CHATFONT,
        HEADERFONT = _G.HEADERFONT,
        CHATFONT_OUTLINE = _G.CHATFONT_OUTLINE,
    }

    local function ApplyLocalizedFonts()
        _G.DEFAULTFONT = FontNames.DEFAULTFONT
        _G.DIALOGFONT = FontNames.DIALOGFONT
        _G.TITLEFONT = FontNames.TITLEFONT
        _G.UIFONT = FontNames.UIFONT
        _G.BUTTONFONT = FontNames.BUTTONFONT
        _G.NEWFONT = FontNames.NEWFONT
        _G.NEWFONT_SMALL = FontNames.NEWFONT_SMALL
        _G.NEWFONT_OUTLINE = FontNames.NEWFONT_OUTLINE
        _G.NEWFONT_OUTLINE_SMALL = FontNames.NEWFONT_OUTLINE_SMALL
        _G.NUMBERFONT = FontNames.NUMBERFONT
        _G.TALKINGFONT = FontNames.TALKINGFONT
        _G.SMALLNUMBERFONT = FontNames.SMALLNUMBERFONT
        _G.BODYTEXTFONT = FontNames.BODYTEXTFONT
        _G.CODEFONT = FontNames.CODEFONT
        _G.TALKINGFONT_WORMWOOD = FontNames.TALKINGFONT_WORMWOOD
        _G.CHATFONT = FontNames.CHATFONT
        _G.HEADERFONT = FontNames.HEADERFONT
        _G.CHATFONT_OUTLINE = FontNames.CHATFONT_OUTLINE

        _G.TheSim:UnloadFont("normalfont")
        _G.TheSim:UnloadFont("normalfont_outline")
        _G.TheSim:UnloadPrefabs({"cn_fonts_"..modname})

        local LocalizedFontAssets = {}
        table.insert(LocalizedFontAssets, _G.Asset("FONT", MODROOT.."fonts/"..font_name..".zip"))
        table.insert(LocalizedFontAssets, _G.Asset("FONT", MODROOT.."fonts/"..font_name_outline..".zip"))

        local LocalizedFontsPrefab = _G.Prefab("common/cn_fonts_"..modname, nil, LocalizedFontAssets)
        _G.RegisterPrefabs(LocalizedFontsPrefab)
        _G.TheSim:LoadPrefabs({"cn_fonts_"..modname})

        _G.TheSim:LoadFont(MODROOT.."fonts/"..font_name..".zip", "normalfont")
        _G.TheSim:LoadFont(MODROOT.."fonts/"..font_name_outline..".zip", "normalfont_outline")

        _G.TheSim:SetupFontFallbacks("normalfont", _G.DEFAULT_FALLBACK_TABLE)
        _G.TheSim:SetupFontFallbacks("normalfont_outline", _G.DEFAULT_FALLBACK_TABLE_OUTLINE)

        _G.DEFAULTFONT = "normalfont_outline"
        _G.DIALOGFONT = "normalfont_outline"
        _G.TITLEFONT = "normalfont_outline"
        _G.UIFONT = "normalfont_outline"
        _G.BUTTONFONT = "normalfont"
        _G.NEWFONT = "normalfont"
        _G.NEWFONT_SMALL = "normalfont"
        _G.NEWFONT_OUTLINE = "normalfont_outline"
        _G.NEWFONT_OUTLINE_SMALL = "normalfont_outline"
        _G.NUMBERFONT = "normalfont_outline"
        _G.TALKINGFONT = "normalfont_outline"
        _G.SMALLNUMBERFONT = "normalfont_outline"
        _G.BODYTEXTFONT = "normalfont_outline"
        _G.CODEFONT = "normalfont"
        _G.TALKINGFONT_WORMWOOD = "normalfont_outline"
        _G.CHATFONT = "normalfont"
        _G.HEADERFONT = "normalfont"
        _G.CHATFONT_OUTLINE = "normalfont"
    end

    _G.getmetatable(_G.TheSim).__index.UnregisterAllPrefabs = (function()
        local oldUnregisterAllPrefabs = _G.getmetatable(_G.TheSim).__index.UnregisterAllPrefabs
        return function(self, ...)
            oldUnregisterAllPrefabs(self, ...)
            ApplyLocalizedFonts()
        end
    end)()

    local OldRegisterPrefabs = _G.ModManager.RegisterPrefabs
    local function NewRegisterPrefabs(self)
        OldRegisterPrefabs(self)
        ApplyLocalizedFonts()
    end
    _G.ModManager.RegisterPrefabs = NewRegisterPrefabs

    local OldStart = _G.Start
    function _G.Start()
        ApplyLocalizedFonts()
        OldStart()
    end
end
