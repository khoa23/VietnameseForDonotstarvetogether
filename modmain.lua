
--LoadPOFile("vietnamese.po", "vi")
--modimport("scripts/main.lua")
mainPOfilename   = "vietnamese.po"
selectedLanguage = "vi"

LoadPOFile(mainPOfilename, selectedLanguage)

io      = GLOBAL.io
assert  = GLOBAL.assert
rawget  = GLOBAL.rawget



-- In order to load a custom language, you must make a .po file for that language and load it here.

--LoadPOFile("ukrainian.po", "ua")

-- More information on this process can be found here: http://forums.kleientertainment.com/index.php?/topic/10292-creating-a-translation-using-the-po-format/
_G = GLOBAL

mods = _G.rawget(_G, "mods")
if not mods then
	mods = {}
	_G.rawset(_G, "mods", mods)
end
_G.mods = mods

mods.UkrainianLang = {
	modinfo = modinfo,
	StorePath = MODROOT,

	MainPoFile = "vietnamese.po",
	SelectedLanguage = "vi"
}

local SelectedLanguage = "vi"

io = _G.io
STRINGS = _G.STRINGS
tonumber = _G.tonumber
tostring = _G.tostring
assert = _G.assert
rawget = _G.rawget
require = _G.require
dumptable = _G.dumptable
deepcopy = _G.deepcopy
TheSim = _G.TheSim
TheNet = _G.TheNet
package = _G.package
rawget = _G.rawget
rawset = _G.rawset

--disabling mode notificaion
_G.getmetatable(TheSim).__index.ShouldWarnModsLoaded = function() 
	return false 
end

_G.ModManager.RegisterPrefabs=NewRegisterPrefabs

modimport("scripts/main.lua")


-------------------------------------------------------------------------------
------------------------------------ UTILS: -----------------------------------
-------------------------------------------------------------------------------

-- split string using given separator
function split(str, sep)
  	local fields, first = {}, 1
	str = str..sep
	for i=1,#str do
		if str:sub(i,i)==sep then
		fields[#fields+1] = str:sub(first,i-1)
			first=i+1
		end
	end
	return fields
end

-------------------------------------------------------------------------------
-------------------- GENERAL ENGINE FOR DEDICATED SERVERS: --------------------
-------------------------------------------------------------------------------

-- 'STRINGS' contains mapping tree with ENG phrases in leaves
STRINGS     = GLOBAL.STRINGS
-- 'STRINGS_NEW' contains mapping: string name -> NEW_LANG
STRINGS_NEW = GLOBAL.LanguageTranslator.languages[selectedLanguage] or {}

DICTIONARY = {}

function buildDictionary(stringsNode, str)
	for i,v in pairs(stringsNode) do
		if type(v)=="table" then
			buildDictionary(stringsNode[i], str.."."..i)
		else
			local val = STRINGS_NEW[str.."."..i]
			if val then
--				print(v, val)
				DICTIONARY[v] = val
			end

		end
	end
end
buildDictionary(STRINGS, "STRINGS")

-------------------------------------------------------------------------------

function translateFromDictionary(s)
	-- the phrase is directly in dictionary
	local tmp = DICTIONARY[s]
	if tmp then return tmp end
	
	
	local function isAcceptableAfterNick(x)
		return (x==' ') or (x==',' ) or (x=='.') or (x=='!') or (x=='?') or (x=='\'') 
	end
	
	-- looking for a phrase with one %s
	local n = s:len()
	local ret, nickLen = nil, n+1
	for i=1,n do
		if i==1 or s:sub(i-1,i-1)==' ' then
			for j = math.min(n,i+nickLen-2),i,-1 do
				if j==n or isAcceptableAfterNick(s:sub(j+1,j+1)) then
					-- subsequence [i,j] can be player's nick
					local x = s:sub(1,i-1).."%s"..s:sub(j+1)
--					print("x1=", x)
					x = DICTIONARY[x]
--					print("x2=", x) ;
					if x then
--						print("cand=", x)
						x = x:gsub("%%s", s:sub(i,j))
						if j-i+1 < nickLen then
							nickLen = j-i+1
							ret = x
						end
					end
				end
			end
		end
	end
	if ret then return ret end
	
	-- looking for a phrase with two %s
	ret, nickLen = nil, n+1
	for i=1,n do
		if i==1 or s:sub(i-1,i-1)==' ' then
			for j = math.min(n,i+nickLen-2),i,-1 do
				if j==n or isAcceptableAfterNick(s:sub(j+1,j+1)) then
					-- subsequence [i,j] can be a player's nick
					for k=j+2,n do
						if s:sub(k-1,k-1)== ' ' then
							for l=k,n do
								if l==n or isAcceptableAfterNick(s:sub(l+1,l+1)) then
									-- subsequence [k,l] can be an attacker
									local x = s:sub(1,i-1).."%s"..s:sub(j+1,k-1).."%s"..s:sub(l+1)
--									print("x1=", x)
									x = DICTIONARY[x]
--									print("x2=", x) ;
									if x then
--										print("cand=", x)
										local attacker = s:sub(k,l)
										attacker = DICTIONARY[attacker] or attacker
										x = x:gsub("%%s", s:sub(i,j), 1)
										x = x:gsub("%%s", attacker)
										if j-i+1 < nickLen then
											nickLen = j-i+1
											ret = x
										end
									end
								end
							end
						end
					end
				end
			end
		end
	end
	if ret then return ret end
	
	-- not found in dictionary
	return s or ""
end

function translateMessage(message)
	local messages = split(message,'\n') or {message}
	local ret=""
	for i=1,#messages do
		local translated=translateFromDictionary(messages[i])
		if i==1 then
			ret = translated
		elseif translated ~= messages[i] then
			ret = ret.."\n"..translated
		else
			ret = ret..translateFromDictionary("\n"..messages[i])
		end
	end
	return ret
end

-------------------------------------------------------------------------------
	
function runTranslatingEngine()
	-- Translating quotes "on the fly" from a dedicated server
	if rawget(GLOBAL,"Networking_Talk") then
		local OldNetworking_Talk=GLOBAL.Networking_Talk

		function Networking_Talk(guid, message, ...)
--			print("Networking_Talk", guid, message, ...)
			message = translateMessage(message)
--			message = "+ "..message.." +"
			if OldNetworking_Talk then OldNetworking_Talk(guid, message, ...) end
		end
		GLOBAL.Networking_Talk=Networking_Talk
	end
	
	
	-- Fixes translating of death annonucements
	local deathAnnonucement = GLOBAL.Networking_DeathAnnouncement
	
	local deathSeparator    =   STRINGS.UI.HUD.DEATH_ANNOUNCEMENT_1
	local deathPossibleEnds = { STRINGS.UI.HUD.DEATH_ANNOUNCEMENT_2_DEFAULT,
		                        STRINGS.UI.HUD.DEATH_ANNOUNCEMENT_2_MALE,
		                        STRINGS.UI.HUD.DEATH_ANNOUNCEMENT_2_FEMALE,
		                        STRINGS.UI.HUD.DEATH_ANNOUNCEMENT_2_ROBOT,
		                        "." }
	
	GLOBAL.Networking_DeathAnnouncement = function(message, ...)		
--		print("MESSAGE: ", message)
		if deathSeparator then
			local k,l = message:find(deathSeparator)
			if k and l then
				for i=1, #deathPossibleEnds do			
					if deathPossibleEnds[i] and message:sub(-deathPossibleEnds[i]:len())==deathPossibleEnds[i] then
						local victim   = message:sub(1,k-2)
						local attacker = message:sub(l+2, -deathPossibleEnds[i]:len()-1)
--						print("VICTIM:   ", victim)
--						print("ATTACKER: ", attacker)
						deathAnnonucement(victim.." "..
						                  (DICTIONARY[deathSeparator] or deathSeparator).." "..
						                  (DICTIONARY[attacker] or attacker)..
						                  (DICTIONARY[deathPossibleEnds[i]] or deathPossibleEnds[i]),
						                  ...)
						return
					end
				end
			end
		end
		deathAnnonucement(message, ...)
	end
	
	
	-- Fixes translating of resurrect announcements
	local resurrectAnnouncement = GLOBAL.Networking_ResurrectAnnouncement
	
	local resSeparator = STRINGS.UI.HUD.REZ_ANNOUNCEMENT
	
	GLOBAL.Networking_ResurrectAnnouncement = function(message, ...)
--		print("MESSAGE: ", message)
		
		-- removing dot for compability with right-to-left languages
		if(message:sub(-1)==".") then
			message = message:sub(1,-2)
		end
		
		if resSeparator then
			local k,l = message:find(resSeparator)
			if k and l then
				local victim   = message:sub(1,k-2)
				local attacker = message:sub(l+2)
--				print("VICTIM:   ", victim)
--				print("ATTACKER: ", attacker)
				resurrectAnnouncement(victim.." "..
				                      (DICTIONARY[resSeparator] or resSeparator).." "..
				                      (DICTIONARY[attacker] or attacker),
				                      ...)
				return
			end
		end
		resurrectAnnouncement(message, ...)
	end
end	
runTranslatingEngine()

