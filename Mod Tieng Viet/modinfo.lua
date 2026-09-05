-- This information tells other players more about the mod
name = "DST Tiếng Việt"
version = "2026.18"
description = "Chuyển đổi ngôn ngữ của game từ tiếng Anh sang tiếng Việt.\n\nCập nhật lần cuối ngày 22/07/2026"
author = "Khoa.ga"

forumthread = ""
-- This lets other players know if your mod is out of date, update it to match the current version in the game
api_version = 10
priority = 9999

-- Can specify a custom icon for this mod!
icon_atlas = "DST_Vietnamese.xml"
icon = "DST_Vietnamese.tex"

server_filter_tags = {"vn", "vietnam", "vietnamese", "viet nam", "khoa.ga"}

dst_compatible = true
all_clients_require_mod = false
client_only_mod = true

configuration_options =
{
	{
		name = "FONT_OPTION",
		label = "Phông chữ (Font)",
		options =	{
						{description = "Nowar", data = "nowar", hover = "Sử dụng phông chữ Nowar (Mặc định)"},
						{description = "Tắt", data = "off", hover = "Không thay đổi phông chữ gốc của game"},
					},
		default = "nowar",
	},
}