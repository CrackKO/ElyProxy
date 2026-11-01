import asyncio
import json
import re
import time
from datetime import datetime
from pathlib import Path
from urllib import error as urllib_error
from urllib import request as urllib_request
from urllib.parse import urlparse, parse_qs

from telegram import (
    Update,
    InlineKeyboardButton,
    InlineKeyboardMarkup,
)
from telegram.ext import (
    Application,
    CommandHandler,
    ContextTypes,
    CallbackQueryHandler,
)

from elyaxis_storage import ensure_elyaxis_data, get_or_create_migration_key

INPUT_FILE = "proxies.txt"
ALIVE_FILE = "alive.txt"
DEAD_FILE = "dead.txt"
TOKEN_FILE = "bot_token.txt"
STATE_FILE = "state.json"
SUBSCRIBERS_FILE = "subscribers.json"
STREAKS_FILE = "streaks.json"
USERS_FILE = "users.json"
WHITELIST_FILE = "whitelist_proxies.json"
USER_PACKS_FILE = "user_packs.json"
API_CONFIG_FILE = "proxy_check_api.json"
ELYAXIS_BOT_URL = "https://t.me/ElyAxisBot"

TIMEOUT = 3
CONCURRENCY = 300
PAGE_SIZE = 10
AUTO_CHECK_INTERVAL_SECONDS = 24 * 60 * 60

OWNER_ID = 574841577
ATLAS_USER_IDS = {
    821249889,
    804585793,
    952707072,
    1098771174,
    499310614,
    1071691184,
    2118377866,
    839793404,
    942261205,
}
ATLAS_SOFT_LINKS = {
    "shadowsocks": "https://elysiumhub.org/safestorage/shadowsocks",
    "happ_ios_global": "https://elysiumhub.org/safestorage/happiosglobal",
    "happ_ios_ru": "https://elysiumhub.org/safestorage/happiosru",
    "happ_android_apk": "https://elysiumhub.org/safestorage/happandroidapk",
    "happ_windows": "https://elysiumhub.org/safestorage/happwin",
}
ATLAS_ELYPROXY_LINKS = {
    "internal": "https://elysiumhub.org/safestorage/elyproxywindows",
    "release": "https://github.com/CrackKO/ElyProxy/releases/tag/v1.4.4",
    "old": "https://github.com/CrackKO/ElyProxy/releases/tag/v1.4.3",
    "repo": "https://github.com/CrackKO/ElyProxy",
}
ATLAS_SHADOWSOCKS_BROWSER_LINKS = {
    "firefox": "https://addons.mozilla.org/ru/firefox/addon/blancvpn-fast-and-secure-proxy/?utm_source=webapp",
    "chrome": "https://chromewebstore.google.com/detail/blancvpn-%E2%80%94-fast-and-secur/fbnnaeejnbpbbkmnieomfamekibceoob?hl=ru",
}
# Replace this value with your target instruction URL.
ATLAS_GUIDE_INSTRUCTION_URL = "https://elysiumdesign.craft.me/zxfU6VPOGjpE26"
ATLAS_DNS_IP = "1.1.1.1"
ATLAS_DNS_DOMAIN = "https://cloudflare-dns.com/dns-query"
ATLAS_ROUTING_RU_URL = r"happ://routing/add/eyJSZW1vdGVETlNUeXBlIjoiRG9VIiwiRG9tZXN0aWNETlNUeXBlIjoiRG9VIiwiR2xvYmFsUHJveHkiOnRydWUsIkdlb3NpdGVVcmwiOiJodHRwczpcL1wvZ2l0aHViLmNvbVwvTG95YWxzb2xkaWVyXC92MnJheS1ydWxlcy1kYXRcL3JlbGVhc2VzXC9sYXRlc3RcL2Rvd25sb2FkXC9nZW9zaXRlLmRhdCIsIkRpcmVjdFNpdGVzIjpbImdlb3NpdGU6Y2F0ZWdvcnktcnUiXSwiRGlyZWN0SXAiOlsiMTAuMC4wLjBcLzgiLCIxMDAuNjQuMC4wXC8xMCIsIjE3Mi4xNi4wLjBcLzEyIiwiMTkyLjE2OC4wLjBcLzE2IiwiMTY5LjI1NC4wLjBcLzE2IiwiMjI0LjAuMC4wXC80IiwiMjU1LjI1NS4yNTUuMjU1IiwiZ2VvaXA6cnUiXSwiRG5zSG9zdHMiOnt9LCJEb21haW5TdHJhdGVneSI6IklQSWZOb25NYXRjaCIsIlByb3h5U2l0ZXMiOltdLCJVc2VDaHVua0ZpbGVzIjp0cnVlLCJSZW1vdGVETlNEb21haW4iOiJodHRwczpcL1wvY2xvdWRmbGFyZS1kbnMuY29tXC9kbnMtcXVlcnkiLCJGYWtlRG5zIjpmYWxzZSwiR2VvaXBVcmwiOiJodHRwczpcL1wvZ2l0aHViLmNvbVwvTG95YWxzb2xkaWVyXC92MnJheS1ydWxlcy1kYXRcL3JlbGVhc2VzXC9sYXRlc3RcL2Rvd25sb2FkXC9nZW9pcC5kYXQiLCJSZW1vdGVETlNJcCI6IjEuMS4xLjEiLCJQcm94eUlwIjpbXSwiRG9tZXN0aWNETlNEb21haW4iOiJodHRwczpcL1wvZG5zLmdvb2dsZVwvZG5zLXF1ZXJ5IiwiQmxvY2tTaXRlcyI6W10sIk5hbWUiOiJZIiwiQmxvY2tJcCI6W10sIkxhc3RVcGRhdGVkIjoxNzc2OTM1NzA3LCJEb21lc3RpY0ROU0lwIjoiOC44LjguOCIsIlJvdXRlT3JkZXIiOiJkaXJlY3QtYmxvY2stcHJveHkifQ=="
ATLAS_TEST_URLS = [
    "https://www.youtube.com",
    "http://captive.apple.com/hotspot-detect.html",
]
ATLAS_VLESS_LINKS = {
    "normal": "https://connliberty.com/connection/subs/21cb52b5-2618-4f7d-b26a-9103478696e2",
    "whitelist": "https://connliberty.com/connection/test_proxies_subs/21cb52b5-2618-4f7d-b26a-9103478696e2",
}
ATLAS_KEY_URL = "https://3fec007e.withblancvpn.online/s/7b2d834d065740e28f1089db40aead57"
ATLAS_LIBERTY_KEY_URL = "https://sub.conn-liberty.net/connection/subs/79e26bf4-b854-4c20-88c5-b0eb80d69f11"
check_lock = asyncio.Lock()
auto_check_task = None

DEFAULT_STATE = {
    "base_updated_at": None,
    "base_originals": [],
    "dead_since_base": [],
    "last_alive_originals": [],
    "last_dead_originals": [],
    "last_check_at": None,
}

DEFAULT_API_CONFIG = {
    "enabled": True,
    "base_url": "https://34map.ru",
    "token": "fastproxyapi",
    "request_timeout_seconds": 180,
    "check_timeout_seconds": TIMEOUT,
    "check_concurrency": CONCURRENCY,
    "fallback_to_local": True,
}

DEFAULT_LANGUAGE = "ru"
SUPPORTED_LANGUAGES = ("ru", "en", "ar")
LANGUAGE_LABELS = {
    "ru": "Русский",
    "en": "English",
    "ar": "العربية",
}

TRANSLATIONS = {
    "ru": {
        "language_name": "Русский",
        "language_saved": "Язык сохранён.",
        "language_title": "🌐 Выбор языка",
        "language_current": "Текущий язык: {value}",
        "menu_text": "Меню.",
        "start_text": "Бот для проверки и раздачи прокси.\n\n",
        "button_quick_connect": "⚡ Быстрое подключение",
        "button_atlas": "🛡 Шапито [Powered Atlas]",
        "button_my_pack": "🎒 Твой пак",
        "button_alive_socks": "🧦 Живые SOCKS5",
        "button_alive_mtproto": "🔐 Живые MTProto",
        "button_pack_all": "📦 Пак всех живых",
        "button_check_now": "🔄 Проверить сейчас",
        "button_dead_since_base": "☠️ Умерли с обновления базы",
        "button_whitelist": "🤍 Белые списки",
        "button_language": "🌐 Язык",
        "button_reload_base": "♻️ Обновить базу",
        "button_users": "👥 Кто пользуется",
        "button_owner_notify_atlas": "📢 Отправить уведомление",
        "button_fastest": "⚡ Самый быстрый",
        "button_my_whitelist": "🤍 Белые списки ({count})",
        "button_my_all_servers": "🗂 Все мои сервера ({count})",
        "button_refresh_pack": "🔄 Обновить пак",
        "button_remove_dead": "☠️ Удалить нерабочие",
        "button_clear_pack": "🧹 Очистить пак",
        "button_menu": "🏠 Меню",
        "button_connect": "🔌 Подключиться {index}",
        "button_connect_fastest": "🔌 Подключиться к самому быстрому",
        "quick_connect_title": "⚡ Самый быстрый сервер сейчас",
        "quick_connect_missing": "Живых прокси для быстрого подключения пока нет.",
        "atlas_title": "🛡 Шапито [Powered Atlas]",
        "atlas_intro": "Приватная панель для участников VPN\n\nЗдесь собрано всё для стабильного подключения:\n\n🔑  Ключи доступа — всегда актуальные\n📦  Софт — под все платформы\n📖  Гайды — пошаговые инструкции\n\nОбновления приходят автоматически.\nВыбери нужный раздел 👇",
        "button_atlas_soft": "📦 Софт",
        "button_atlas_vless": "🔑 Получить Ключ",
        "button_atlas_guides": "📖 Гайды",
        "atlas_soft_title": "📦 Софт",
        "atlas_vless_title": "🔑 Ключ подписки",
        "atlas_vless_copy_hint": "👆 Нажмите на ключ, чтобы скопировать",
        "button_platform_windows": "Windows",
        "button_platform_ios": "IOS",
        "button_platform_android": "Android",
        "button_install": "Установить",
        "atlas_guides_title": "📖 Гайды",
        "button_atlas_soft_shadowsocks": "Скачать расширение BlancVPN",
        "atlas_soft_shadowsocks_title": "Скачать расширение BlancVPN",
        "button_browser_firefox": "🦊 Firefox",
        "button_browser_chrome": "🌐 Chrome",
        "button_atlas_soft_happ_ios_global": "Скачать Happ IOS Global",
        "button_atlas_soft_happ_ios_ru": "Скачать Happ IOS RU",
        "button_atlas_soft_happ_android_apk": "Скачать Happ Android APK",
        "button_atlas_soft_happ_windows": "Скачать Happ Windows",
        "button_atlas_soft_elyproxy_windows": "🪟 ElyProxy Windows",
        "atlas_soft_elyproxy_title": "🪟 ElyProxy Windows",
        "button_atlas_soft_elyproxy_internal": "📥 Скачать с сервера",
        "button_atlas_soft_elyproxy_release": "Свежая (GIT 1.4.4)",
        "button_atlas_soft_elyproxy_old": "Прошлая (GIT 1.4.3)",
        "button_atlas_soft_elyproxy_repo": "🧩 GitHub Репозиторий",
        "button_atlas_vless_normal": "Получить Normal Ключ",
        "button_atlas_vless_whitelist": "Получить Whitelist Ключ",
        "button_atlas_guide_instruction": "📘 Инструкция",
        "button_atlas_guide_text_data": "📝 Текстовые данные",
        "button_atlas_guide_test_url": "🔗 Тестовый URL",
        "button_atlas_guide_routing_ru": "🧭 Маршрутизация RU",
        "atlas_guide_instruction_title": "📘 Инструкция",
        "atlas_guide_text_data_title": "📝 Текстовые данные",
        "atlas_guide_test_url_title": "🔗 Тестовый URL",
        "atlas_guide_routing_ru_title": "🧭 Маршрутизация RU",
        "button_atlas_open_instruction": "▶️ Открыть ссылку",
        "atlas_copy_hint": "📋 Нажмите на значение, чтобы скопировать",
        "atlas_notification_sent": "Уведомление отправлено: {sent}\nОшибок доставки: {failed}",
        "atlas_notification_text": "Прозошло изменение в закрытом меню, пожалуйста проверьте изменения.",
        "button_back": "⬅️ Назад",
        "button_next": "➡️ Дальше",
        "button_back_to_pack": "🎒 Назад в пак",
        "button_confirm_clear": "🧹 Да, очистить",
        "button_cancel": "❌ Отмена",
        "button_add": "➕ Добавить {index}",
        "button_to_my_pack": "📥 В мой пак {index}",
        "button_noop": "— {index}",
        "my_pack_title": "🎒 Твой пак",
        "servers_in_pack": "Серверов в паке: {count}",
        "updated": "Обновлён: {value}",
        "my_pack_whitelist_title": "🎒 Твой пак • Белые списки",
        "my_pack_all_title": "🎒 Твой пак • Все сервера",
        "nothing_here": "Ничего нет.",
        "no_alive_in_pack": "Живых прокси в паке не найдено.",
        "fastest_pack_title": "⚡ Самый быстрый сервер из твоего пака",
        "pack_refreshed": "🎒 Пак обновлён.",
        "total_in_pack": "Всего в паке: {count}",
        "alive_count": "Живых: {count}",
        "dead_count": "Мёртвых: {count}",
        "removed_dead": "🎒 Из пака удалено нерабочих прокси: {count}",
        "clear_pack_confirm": "Точно очистить твой пак?",
        "pack_cleared": "🎒 Твой пак очищен.",
        "status_label": "Статус: {value}",
        "status_alive": "жив ({ms} ms)",
        "status_dead": "мёртв",
        "status_invalid": "битый формат",
        "status_unknown": "не проверен",
        "status_unknown_short": "неизвестно",
        "latency": "Задержка: {ms} ms",
        "add_to_telegram": "Добавить в Telegram: {value}",
        "yes": "Да",
        "no": "Нет",
        "whitelist_title": "🤍 Прокси: Белые списки",
        "whitelist_empty": "Список пуст.",
        "summary_done": "Проверка завершена.",
        "total": "Всего: {count}",
        "alive_total": "Живых: {count}",
        "dead_total": "Мёртвых: {count}",
        "socks_alive": "SOCKS5 живых: {count}",
        "mtproto_alive": "MTProto живых: {count}",
        "base_updated": "База обновлена: {value}",
        "last_check": "Последняя проверка: {value}",
        "dead_since_base_count": "Умерли с обновления базы: {count}",
        "top_fastest": "Топ 5 самых быстрых (по серверному замеру):",
        "pack_empty": "{title}\n\nНичего нет.",
        "pack_add": "Добавить: {url}",
        "alive_socks_title": "🧦 Живые SOCKS5",
        "alive_mtproto_title": "🔐 Живые MTProto",
        "alive_generic_title": "Живые",
        "dead_since_base_title": "☠️ Умерли с обновления базы",
        "users_empty": "👥 Пользователей пока нет.",
        "users_title": "👥 Кто пользуется ботом",
        "total_users": "Всего пользователей: {count}",
        "no_username": "без username",
        "name_label": "Имя: {value}",
        "last_login": "Последний вход: {value}",
        "proxy_list_updated": "📢 Список прокси обновлён.\nБаза обновлена: {value}",
        "check_already_running": "Проверка уже идёт. Подожди.",
        "check_starting": "Запускаю проверку прокси...",
        "check_error": "Во время проверки произошла ошибка:\n{error}",
        "unavailable": "Недоступно",
        "wait_current_check": "Сейчас уже идёт проверка, дождись окончания.",
        "reload_starting": "Обновляю базу и запускаю новую проверку...",
        "reload_error": "Ошибка при обновлении базы:\n{error}",
        "base_updated_done": "База прокси обновлена.\nДата обновления базы: {value}",
        "private_only": "Управление доступно только в личке с ботом.",
        "press_check_first": "Сначала нажми кнопку проверки.",
        "proxy_not_found": "Не удалось найти прокси.",
        "proxy_already_in_pack": "Этот прокси уже есть в твоём паке.",
        "proxy_added_to_pack": "Прокси добавлен в твой пак.",
    },
    "en": {
        "language_name": "English",
        "language_saved": "Language saved.",
        "language_title": "🌐 Choose Language",
        "language_current": "Current language: {value}",
        "menu_text": "Menu.",
        "start_text": "Bot for checking and sharing proxies.\n\n",
        "button_quick_connect": "⚡ Quick Connect",
        "button_atlas": "Shapito [Powered Atlas]",
        "button_my_pack": "🎒 My Pack",
        "button_alive_socks": "🧦 Live SOCKS5",
        "button_alive_mtproto": "🔐 Live MTProto",
        "button_pack_all": "📦 Pack of All Live Proxies",
        "button_check_now": "🔄 Check Now",
        "button_dead_since_base": "☠️ Died Since Base Update",
        "button_whitelist": "🤍 Whitelist",
        "button_language": "🌐 Language",
        "button_reload_base": "♻️ Reload Base",
        "button_users": "👥 Active Users",
        "button_owner_notify_atlas": "📢 Send Notification",
        "button_fastest": "⚡ Fastest",
        "button_my_whitelist": "🤍 Whitelist ({count})",
        "button_my_all_servers": "🗂 All My Servers ({count})",
        "button_refresh_pack": "🔄 Refresh Pack",
        "button_remove_dead": "☠️ Remove Dead",
        "button_clear_pack": "🧹 Clear Pack",
        "button_menu": "🏠 Menu",
        "button_connect": "🔌 Connect {index}",
        "button_connect_fastest": "🔌 Connect to the Fastest",
        "quick_connect_title": "⚡ Fastest Server Right Now",
        "quick_connect_missing": "No live proxies are available for quick connect yet.",
        "atlas_title": "🛡 Shapito [Powered Atlas]",
        "atlas_intro": "Private panel for VPN members\n\nEverything you need for a stable connection:\n\n🔑  Access keys — always up to date\n📦  Software — for all platforms\n📖  Guides — step-by-step instructions\n\nUpdates arrive automatically.\nChoose a section below 👇",
        "button_atlas_soft": "📦 Software",
        "button_atlas_vless": "🔑 Get Key",
        "button_atlas_guides": "📖 Guides",
        "atlas_soft_title": "📦 Software",
        "atlas_vless_title": "🔑 Subscription Key",
        "atlas_vless_copy_hint": "👆 Tap the key to copy",
        "button_platform_windows": "Windows",
        "button_platform_ios": "IOS",
        "button_platform_android": "Android",
        "button_install": "Install",
        "atlas_guides_title": "📖 Guides",
        "button_atlas_soft_shadowsocks": "Download Shadowsocks",
        "atlas_soft_shadowsocks_title": "Shadowsocks • Choose browser",
        "button_browser_firefox": "🦊 Firefox",
        "button_browser_chrome": "🌐 Chrome",
        "button_atlas_soft_happ_ios_global": "Download Happ iOS Global",
        "button_atlas_soft_happ_ios_ru": "Download Happ iOS RU",
        "button_atlas_soft_happ_android_apk": "Download Happ Android APK",
        "button_atlas_soft_happ_windows": "Download Happ Windows",
        "button_atlas_soft_elyproxy_windows": "🪟 ElyProxy Windows",
        "atlas_soft_elyproxy_title": "🪟 ElyProxy Windows",
        "button_atlas_soft_elyproxy_internal": "📥 Download from internal server",
        "button_atlas_soft_elyproxy_release": "Latest (GIT 1.4.4)",
        "button_atlas_soft_elyproxy_old": "Previous (GIT 1.4.3)",
        "button_atlas_soft_elyproxy_repo": "🧩 GitHub Repository",
        "button_atlas_vless_normal": "Get Normal Key",
        "button_atlas_vless_whitelist": "Get Whitelist Key",
        "button_atlas_guide_instruction": "📘 Instruction",
        "button_atlas_guide_text_data": "Text Data",
        "button_atlas_guide_test_url": "Test URL",
        "button_atlas_guide_routing_ru": "Routing RU",
        "atlas_guide_instruction_title": "📘 Instruction",
        "atlas_guide_text_data_title": "Text Data",
        "atlas_guide_test_url_title": "Test URL",
        "atlas_guide_routing_ru_title": "Routing RU",
        "button_atlas_open_instruction": "▶️ Open Link",
        "atlas_copy_hint": "Tap a value to copy",
        "atlas_notification_sent": "Notification sent: {sent}\nDelivery errors: {failed}",
        "atlas_notification_text": "There was a change in the closed menu, please check the updates.",
        "button_back": "⬅️ Back",
        "button_next": "➡️ Next",
        "button_back_to_pack": "🎒 Back to Pack",
        "button_confirm_clear": "🧹 Yes, clear it",
        "button_cancel": "❌ Cancel",
        "button_add": "➕ Add {index}",
        "button_to_my_pack": "📥 To My Pack {index}",
        "button_noop": "— {index}",
        "my_pack_title": "🎒 My Pack",
        "servers_in_pack": "Servers in pack: {count}",
        "updated": "Updated: {value}",
        "my_pack_whitelist_title": "🎒 My Pack • Whitelist",
        "my_pack_all_title": "🎒 My Pack • All Servers",
        "nothing_here": "Nothing here.",
        "no_alive_in_pack": "No live proxies found in your pack.",
        "fastest_pack_title": "⚡ Fastest server in your pack",
        "pack_refreshed": "🎒 Pack refreshed.",
        "total_in_pack": "Total in pack: {count}",
        "alive_count": "Alive: {count}",
        "dead_count": "Dead: {count}",
        "removed_dead": "🎒 Dead proxies removed from pack: {count}",
        "clear_pack_confirm": "Clear your pack?",
        "pack_cleared": "🎒 Your pack has been cleared.",
        "status_label": "Status: {value}",
        "status_alive": "alive ({ms} ms)",
        "status_dead": "dead",
        "status_invalid": "invalid format",
        "status_unknown": "not checked",
        "status_unknown_short": "unknown",
        "latency": "Latency: {ms} ms",
        "add_to_telegram": "Add to Telegram: {value}",
        "yes": "Yes",
        "no": "No",
        "whitelist_title": "🤍 Proxies: Whitelist",
        "whitelist_empty": "The list is empty.",
        "summary_done": "Check completed.",
        "total": "Total: {count}",
        "alive_total": "Alive: {count}",
        "dead_total": "Dead: {count}",
        "socks_alive": "SOCKS5 alive: {count}",
        "mtproto_alive": "MTProto alive: {count}",
        "base_updated": "Base updated: {value}",
        "last_check": "Last check: {value}",
        "dead_since_base_count": "Died since base update: {count}",
        "top_fastest": "Top 5 fastest (server-side check):",
        "pack_empty": "{title}\n\nNothing here.",
        "pack_add": "Add: {url}",
        "alive_socks_title": "🧦 Live SOCKS5",
        "alive_mtproto_title": "🔐 Live MTProto",
        "alive_generic_title": "Alive",
        "dead_since_base_title": "☠️ Died Since Base Update",
        "users_empty": "👥 No users yet.",
        "users_title": "👥 Users of the bot",
        "total_users": "Total users: {count}",
        "no_username": "no username",
        "name_label": "Name: {value}",
        "last_login": "Last seen: {value}",
        "proxy_list_updated": "📢 Proxy list updated.\nBase updated: {value}",
        "check_already_running": "A check is already running. Please wait.",
        "check_starting": "Starting proxy check...",
        "check_error": "An error occurred during the check:\n{error}",
        "unavailable": "Unavailable",
        "wait_current_check": "A check is already in progress. Please wait for it to finish.",
        "reload_starting": "Reloading the base and starting a fresh check...",
        "reload_error": "Error while reloading the base:\n{error}",
        "base_updated_done": "Proxy base updated.\nBase update time: {value}",
        "private_only": "Controls are available only in a private chat with the bot.",
        "press_check_first": "Press the check button first.",
        "proxy_not_found": "Could not find that proxy.",
        "proxy_already_in_pack": "This proxy is already in your pack.",
        "proxy_added_to_pack": "Proxy added to your pack.",
    },
    "ar": {
        "language_name": "العربية",
        "language_saved": "تم حفظ اللغة.",
        "language_title": "🌐 اختيار اللغة",
        "language_current": "اللغة الحالية: {value}",
        "menu_text": "القائمة.",
        "start_text": "بوت لفحص البروكسيات ومشاركتها.\n\n",
        "button_quick_connect": "⚡ اتصال سريع",
        "button_atlas": "Shapito [Powered Atlas]",
        "button_my_pack": "🎒 باقتي",
        "button_alive_socks": "🧦 SOCKS5 العاملة",
        "button_alive_mtproto": "🔐 MTProto العاملة",
        "button_pack_all": "📦 باقة كل البروكسيات العاملة",
        "button_check_now": "🔄 افحص الآن",
        "button_dead_since_base": "☠️ التي تعطلت منذ تحديث القاعدة",
        "button_whitelist": "🤍 القائمة البيضاء",
        "button_language": "🌐 اللغة",
        "button_reload_base": "♻️ تحديث القاعدة",
        "button_users": "👥 المستخدمون",
        "button_owner_notify_atlas": "📢 إرسال إشعار",
        "button_fastest": "⚡ الأسرع",
        "button_my_whitelist": "🤍 القائمة البيضاء ({count})",
        "button_my_all_servers": "🗂 كل خوادمي ({count})",
        "button_refresh_pack": "🔄 تحديث الباقة",
        "button_remove_dead": "☠️ حذف المعطلة",
        "button_clear_pack": "🧹 مسح الباقة",
        "button_menu": "🏠 القائمة",
        "button_connect": "🔌 اتصال {index}",
        "button_connect_fastest": "🔌 الاتصال بالأسرع",
        "quick_connect_title": "⚡ أسرع خادم الآن",
        "quick_connect_missing": "لا توجد بروكسيات عاملة للاتصال السريع حالياً.",
        "atlas_title": "🛡 Shapito [Powered Atlas]",
        "atlas_intro": "لوحة خاصة لأعضاء VPN\n\nكل ما تحتاجه لاتصال مستقر:\n\n🔑  مفاتيح الوصول — محدثة دائماً\n📦  البرامج — لجميع المنصات\n📖  الأدلة — تعليمات خطوة بخطوة\n\nتصل التحديثات تلقائياً.\nاختر القسم أدناه 👇",
        "button_atlas_soft": "📦 البرامج",
        "button_atlas_vless": "🔑 الحصول على المفتاح",
        "button_atlas_guides": "📖 الأدلة",
        "atlas_soft_title": "📦 البرامج",
        "atlas_vless_title": "🔑 مفتاح الاشتراك",
        "atlas_vless_copy_hint": "👆 اضغط على المفتاح للنسخ",
        "button_platform_windows": "Windows",
        "button_platform_ios": "IOS",
        "button_platform_android": "Android",
        "button_install": "Install",
        "atlas_guides_title": "📖 الأدلة",
        "button_atlas_soft_shadowsocks": "تنزيل Shadowsocks",
        "atlas_soft_shadowsocks_title": "Shadowsocks • اختر المتصفح",
        "button_browser_firefox": "🦊 Firefox",
        "button_browser_chrome": "🌐 Chrome",
        "button_atlas_soft_happ_ios_global": "تنزيل Happ IOS Global",
        "button_atlas_soft_happ_ios_ru": "تنزيل Happ IOS RU",
        "button_atlas_soft_happ_android_apk": "تنزيل Happ Android APK",
        "button_atlas_soft_happ_windows": "تنزيل Happ Windows",
        "button_atlas_soft_elyproxy_windows": "🪟 ElyProxy Windows",
        "atlas_soft_elyproxy_title": "🪟 ElyProxy Windows",
        "button_atlas_soft_elyproxy_internal": "📥 تنزيل من الخادم الداخلي",
        "button_atlas_soft_elyproxy_release": "Latest (GIT 1.4.4)",
        "button_atlas_soft_elyproxy_old": "Previous (GIT 1.4.3)",
        "button_atlas_soft_elyproxy_repo": "🧩 مستودع GitHub",
        "button_atlas_vless_normal": "الحصول على المفتاح العادي",
        "button_atlas_vless_whitelist": "الحصول على مفتاح القائمة البيضاء",
        "button_atlas_guide_instruction": "📘 تعليمات",
        "button_atlas_guide_text_data": "بيانات نصية",
        "button_atlas_guide_test_url": "رابط اختبار",
        "button_atlas_guide_routing_ru": "توجيه RU",
        "atlas_guide_instruction_title": "📘 تعليمات",
        "atlas_guide_text_data_title": "بيانات نصية",
        "atlas_guide_test_url_title": "رابط اختبار",
        "atlas_guide_routing_ru_title": "توجيه RU",
        "button_atlas_open_instruction": "▶️ فتح الرابط",
        "atlas_copy_hint": "اضغط على القيمة لنسخها",
        "atlas_notification_sent": "تم إرسال الإشعار: {sent}\nأخطاء التوصيل: {failed}",
        "atlas_notification_text": "حدث تغيير في القائمة المغلقة، يرجى التحقق من التحديثات.",
        "button_back": "⬅️ رجوع",
        "button_next": "➡️ التالي",
        "button_back_to_pack": "🎒 العودة للباقة",
        "button_confirm_clear": "🧹 نعم، امسحها",
        "button_cancel": "❌ إلغاء",
        "button_add": "➕ إضافة {index}",
        "button_to_my_pack": "📥 إلى باقتي {index}",
        "button_noop": "— {index}",
        "my_pack_title": "🎒 باقتي",
        "servers_in_pack": "عدد الخوادم في الباقة: {count}",
        "updated": "آخر تحديث: {value}",
        "my_pack_whitelist_title": "🎒 باقتي • القائمة البيضاء",
        "my_pack_all_title": "🎒 باقتي • كل الخوادم",
        "nothing_here": "لا يوجد شيء هنا.",
        "no_alive_in_pack": "لم يتم العثور على بروكسيات عاملة في باقتك.",
        "fastest_pack_title": "⚡ أسرع خادم في باقتك",
        "pack_refreshed": "🎒 تم تحديث الباقة.",
        "total_in_pack": "إجمالي الباقة: {count}",
        "alive_count": "العاملة: {count}",
        "dead_count": "المعطلة: {count}",
        "removed_dead": "🎒 تم حذف البروكسيات المعطلة من الباقة: {count}",
        "clear_pack_confirm": "هل تريد مسح باقتك؟",
        "pack_cleared": "🎒 تم مسح باقتك.",
        "status_label": "الحالة: {value}",
        "status_alive": "يعمل ({ms} ms)",
        "status_dead": "متعطل",
        "status_invalid": "صيغة غير صالحة",
        "status_unknown": "لم يتم فحصه",
        "status_unknown_short": "غير معروف",
        "latency": "الاستجابة: {ms} ms",
        "add_to_telegram": "إضافة إلى تيليجرام: {value}",
        "yes": "نعم",
        "no": "لا",
        "whitelist_title": "🤍 البروكسيات: القائمة البيضاء",
        "whitelist_empty": "القائمة فارغة.",
        "summary_done": "اكتمل الفحص.",
        "total": "الإجمالي: {count}",
        "alive_total": "العاملة: {count}",
        "dead_total": "المعطلة: {count}",
        "socks_alive": "SOCKS5 العاملة: {count}",
        "mtproto_alive": "MTProto العاملة: {count}",
        "base_updated": "تم تحديث القاعدة: {value}",
        "last_check": "آخر فحص: {value}",
        "dead_since_base_count": "تعطلت منذ تحديث القاعدة: {count}",
        "top_fastest": "أسرع 5 بروكسيات (بحسب فحص الخادم):",
        "pack_empty": "{title}\n\nلا يوجد شيء هنا.",
        "pack_add": "إضافة: {url}",
        "alive_socks_title": "🧦 SOCKS5 العاملة",
        "alive_mtproto_title": "🔐 MTProto العاملة",
        "alive_generic_title": "العاملة",
        "dead_since_base_title": "☠️ التي تعطلت منذ تحديث القاعدة",
        "users_empty": "👥 لا يوجد مستخدمون بعد.",
        "users_title": "👥 مستخدمو البوت",
        "total_users": "إجمالي المستخدمين: {count}",
        "no_username": "بدون اسم مستخدم",
        "name_label": "الاسم: {value}",
        "last_login": "آخر دخول: {value}",
        "proxy_list_updated": "📢 تم تحديث قائمة البروكسيات.\nتم تحديث القاعدة: {value}",
        "check_already_running": "هناك فحص جارٍ بالفعل. يرجى الانتظار.",
        "check_starting": "جارٍ بدء فحص البروكسيات...",
        "check_error": "حدث خطأ أثناء الفحص:\n{error}",
        "unavailable": "غير متاح",
        "wait_current_check": "هناك فحص جارٍ الآن، انتظر حتى ينتهي.",
        "reload_starting": "جارٍ تحديث القاعدة وبدء فحص جديد...",
        "reload_error": "حدث خطأ أثناء تحديث القاعدة:\n{error}",
        "base_updated_done": "تم تحديث قاعدة البروكسي.\nوقت تحديث القاعدة: {value}",
        "private_only": "التحكم متاح فقط في المحادثة الخاصة مع البوت.",
        "press_check_first": "اضغط زر الفحص أولاً.",
        "proxy_not_found": "تعذر العثور على هذا البروكسي.",
        "proxy_already_in_pack": "هذا البروكسي موجود بالفعل في باقتك.",
        "proxy_added_to_pack": "تمت إضافة البروكسي إلى باقتك.",
    },
}


def normalize_language(language: str | None) -> str:
    if language in SUPPORTED_LANGUAGES:
        return language
    return DEFAULT_LANGUAGE


def tr(lang_code: str, key: str, **kwargs) -> str:
    lang_code = normalize_language(lang_code)
    template = TRANSLATIONS.get(lang_code, {}).get(key)
    if template is None:
        template = TRANSLATIONS[DEFAULT_LANGUAGE].get(key, key)
    return template.format(**kwargs)


def empty_last_result() -> dict:
    return {
        "total": 0,
        "alive_count": 0,
        "dead_count": 0,
        "socks_alive": 0,
        "mtproto_alive": 0,
        "unknown_alive": 0,
        "alive": [],
        "dead": [],
        "checked": False,
    }


last_result = empty_last_result()


def now_str():
    return datetime.now().strftime("%d.%m.%Y %H:%M:%S")


def ensure_files():
    Path(INPUT_FILE).touch(exist_ok=True)
    Path(ALIVE_FILE).touch(exist_ok=True)
    Path(DEAD_FILE).touch(exist_ok=True)
    Path(TOKEN_FILE).touch(exist_ok=True)
    ensure_elyaxis_data()

    if not Path(STATE_FILE).exists():
        Path(STATE_FILE).write_text(
            json.dumps(DEFAULT_STATE, ensure_ascii=False, indent=2),
            encoding="utf-8"
        )

    if not Path(WHITELIST_FILE).exists():
        Path(WHITELIST_FILE).write_text("[]", encoding="utf-8")

    if not Path(SUBSCRIBERS_FILE).exists():
        Path(SUBSCRIBERS_FILE).write_text("[]", encoding="utf-8")

    if not Path(STREAKS_FILE).exists():
        Path(STREAKS_FILE).write_text("{}", encoding="utf-8")

    if not Path(USERS_FILE).exists():
        Path(USERS_FILE).write_text("{}", encoding="utf-8")

    if not Path(USER_PACKS_FILE).exists():
        Path(USER_PACKS_FILE).write_text("{}", encoding="utf-8")

    if not Path(API_CONFIG_FILE).exists():
        Path(API_CONFIG_FILE).write_text(
            json.dumps(DEFAULT_API_CONFIG, ensure_ascii=False, indent=2),
            encoding="utf-8"
        )

def is_private_chat(update: Update) -> bool:
    return bool(update.effective_chat and update.effective_chat.type == "private")

def load_whitelist_proxies() -> list[str]:
    try:
        data = json.loads(Path(WHITELIST_FILE).read_text(encoding="utf-8"))
        if isinstance(data, list):
            return [str(x).strip() for x in data if str(x).strip()]
    except Exception:
        pass
    return []


def save_whitelist_proxies(items: list[str]):
    cleaned = []
    seen = set()

    for item in items:
        item = str(item).strip()
        if not item or item in seen:
            continue
        seen.add(item)
        cleaned.append(item)

    Path(WHITELIST_FILE).write_text(
        json.dumps(cleaned, ensure_ascii=False, indent=2),
        encoding="utf-8"
    )

def is_owner(update: Update) -> bool:
    return bool(update.effective_user and update.effective_user.id == OWNER_ID)


def has_atlas_access(update: Update | None) -> bool:
    return bool(
        update
        and update.effective_user
        and (update.effective_user.id == OWNER_ID or update.effective_user.id in ATLAS_USER_IDS)
    )


def load_token() -> str:
    token = Path(TOKEN_FILE).read_text(encoding="utf-8").strip()
    if not token:
        raise RuntimeError("Файл bot_token.txt пуст. Вставь туда токен от BotFather.")
    return token


def to_bool(value, default: bool = False) -> bool:
    if isinstance(value, bool):
        return value
    if value is None:
        return default
    if isinstance(value, str):
        value = value.strip().lower()
        if value in {"1", "true", "yes", "on"}:
            return True
        if value in {"0", "false", "no", "off"}:
            return False
    return bool(value)


def load_api_config() -> dict:
    merged = DEFAULT_API_CONFIG.copy()

    try:
        data = json.loads(Path(API_CONFIG_FILE).read_text(encoding="utf-8"))
        if isinstance(data, dict):
            merged.update(data)
    except Exception:
        pass

    merged["enabled"] = to_bool(merged.get("enabled"), False)
    merged["fallback_to_local"] = to_bool(merged.get("fallback_to_local"), True)
    merged["base_url"] = str(merged.get("base_url", "")).strip().rstrip("/")
    merged["token"] = str(merged.get("token", "")).strip()

    for key, default in (
        ("request_timeout_seconds", DEFAULT_API_CONFIG["request_timeout_seconds"]),
        ("check_timeout_seconds", DEFAULT_API_CONFIG["check_timeout_seconds"]),
        ("check_concurrency", DEFAULT_API_CONFIG["check_concurrency"]),
    ):
        try:
            merged[key] = max(1, int(merged.get(key, default)))
        except Exception:
            merged[key] = default

    return merged


def load_state() -> dict:
    try:
        data = json.loads(Path(STATE_FILE).read_text(encoding="utf-8"))
        if isinstance(data, dict):
            merged = DEFAULT_STATE.copy()
            merged.update(data)
            return merged
    except Exception:
        pass
    return DEFAULT_STATE.copy()


def save_state(state: dict):
    Path(STATE_FILE).write_text(
        json.dumps(state, ensure_ascii=False, indent=2),
        encoding="utf-8"
    )


def load_streaks() -> dict:
    try:
        data = json.loads(Path(STREAKS_FILE).read_text(encoding="utf-8"))
        if isinstance(data, dict):
            return data
    except Exception:
        pass
    return {}


def save_streaks(streaks: dict):
    Path(STREAKS_FILE).write_text(
        json.dumps(streaks, ensure_ascii=False, indent=2),
        encoding="utf-8"
    )


def reset_proxy_tracking():
    global last_result

    Path(ALIVE_FILE).write_text("", encoding="utf-8")
    Path(DEAD_FILE).write_text("", encoding="utf-8")
    save_state(DEFAULT_STATE.copy())
    save_streaks({})
    last_result = empty_last_result()


def load_users() -> dict:
    try:
        data = json.loads(Path(USERS_FILE).read_text(encoding="utf-8"))
        if isinstance(data, dict):
            for item in data.values():
                if isinstance(item, dict):
                    item["language"] = normalize_language(item.get("language"))
            return data
    except Exception:
        pass
    return {}


def save_users(users: dict):
    Path(USERS_FILE).write_text(
        json.dumps(users, ensure_ascii=False, indent=2),
        encoding="utf-8"
    )

def load_user_packs() -> dict:
    try:
        data = json.loads(Path(USER_PACKS_FILE).read_text(encoding="utf-8"))
        if isinstance(data, dict):
            return data
    except Exception:
        pass
    return {}


def save_user_packs(data: dict):
    Path(USER_PACKS_FILE).write_text(
        json.dumps(data, ensure_ascii=False, indent=2),
        encoding="utf-8"
    )


def get_user_pack(user_id: int) -> dict:
    packs = load_user_packs()
    key = str(user_id)
    if key not in packs:
        packs[key] = {
            "user_id": user_id,
            "username": "",
            "full_name": "",
            "updated_at": None,
            "items": []
        }
        save_user_packs(packs)
    return packs[key]


def save_user_pack_from_update(update: Update, pack: dict):
    packs = load_user_packs()
    user = update.effective_user
    key = str(user.id)

    pack["user_id"] = user.id
    pack["username"] = user.username or ""
    pack["full_name"] = user.full_name or ""
    pack["updated_at"] = now_str()

    packs[key] = pack
    save_user_packs(packs)

def get_user_language(user_id: int | None) -> str:
    if user_id is None:
        return DEFAULT_LANGUAGE
    user = load_users().get(str(user_id), {})
    return normalize_language(user.get("language"))


def get_update_language(update: Update | None) -> str:
    if update and update.effective_user:
        return get_user_language(update.effective_user.id)
    return DEFAULT_LANGUAGE


def set_user_language(update: Update, language: str):
    remember_user(update, language=language)


def add_proxy_to_my_pack(update: Update, proxy_line: str, source: str) -> tuple[bool, str]:
    user = update.effective_user
    pack = get_user_pack(user.id)
    lang = get_update_language(update)

    items = pack.get("items", [])
    for item in items:
        if item.get("proxy") == proxy_line:
            return False, tr(lang, "proxy_already_in_pack")

    items.append({
        "proxy": proxy_line,
        "source": source,               # socks / mtproto / whitelist
        "added_at": now_str(),
        "last_status": "unknown",       # alive / dead / unknown / invalid
        "last_ms": None,
        "last_checked_at": None,
    })

    pack["items"] = items
    save_user_pack_from_update(update, pack)
    return True, tr(lang, "proxy_added_to_pack")

async def refresh_user_pack(update: Update):
    user = update.effective_user
    pack = get_user_pack(user.id)
    items = pack.get("items", [])

    if not items:
        return pack

    valid_originals = []

    for item in items:
        item["last_checked_at"] = now_str()

        parsed = parse_proxy_line(item["proxy"])
        if parsed is None:
            item["last_status"] = "invalid"
            item["last_ms"] = None
            item["proxy_type"] = "unknown"
            item["ip"] = "?"
            item["port"] = "?"
            continue

        original, ip, port, proxy_type = parsed
        item["proxy_type"] = proxy_type
        item["ip"] = ip
        item["port"] = port
        valid_originals.append(original)

    if valid_originals:
        result = await run_proxy_check_for_lines(valid_originals)
        alive_map = {entry[0]: entry for entry in result["alive"]}
        dead_map = {entry[0]: entry for entry in result["dead"]}

        for item in items:
            parsed = parse_proxy_line(item["proxy"])
            if parsed is None:
                continue

            original = parsed[0]
            alive_entry = alive_map.get(original)
            if alive_entry:
                _, proxy_type, ip, port, ms = alive_entry
                item["last_status"] = "alive"
                item["last_ms"] = ms
                item["proxy_type"] = proxy_type
                item["ip"] = ip
                item["port"] = port
                continue

            dead_entry = dead_map.get(original)
            if dead_entry:
                _, proxy_type, ip, port = dead_entry
                item["last_status"] = "dead"
                item["last_ms"] = None
                item["proxy_type"] = proxy_type
                item["ip"] = ip
                item["port"] = port
                continue

            item["last_status"] = "unknown"
            item["last_ms"] = None

    updated_items = list(items)

    # живые сверху, потом по ms
    def sort_key(x):
        status_order = 0 if x.get("last_status") == "alive" else 1
        ms = x.get("last_ms")
        return (status_order, ms if ms is not None else 999999)

    updated_items.sort(key=sort_key)

    pack["items"] = updated_items
    save_user_pack_from_update(update, pack)
    return pack

def format_my_pack_item(item: dict, lang: str, index: int | None = None):
    prefix = f"{index}. " if index is not None else ""
    proxy_line = item.get("proxy", "")
    parsed = parse_proxy_line(proxy_line)

    if parsed:
        _, ip, port, proxy_type = parsed
    else:
        ip, port, proxy_type = "?", "?", "unknown"

    status = item.get("last_status", "unknown")
    ms = item.get("last_ms")

    if status == "alive":
        icon = "✅"
        status_text = tr(lang, "status_alive", ms=ms)
    elif status == "dead":
        icon = "☠️"
        status_text = tr(lang, "status_dead")
    elif status == "invalid":
        icon = "⚠️"
        status_text = tr(lang, "status_invalid")
    else:
        icon = "❓"
        status_text = tr(lang, "status_unknown")

    add_url = get_add_url(proxy_line, proxy_type)
    addable = tr(lang, "yes") if add_url else tr(lang, "no")

    return (
        f"{prefix}{icon} [{proxy_type}] {ip}:{port}\n"
        f"{tr(lang, 'status_label', value=status_text)}\n"
        f"{tr(lang, 'add_to_telegram', value=addable)}"
    )

def remember_user(update: Update, language: str | None = None):
    user = update.effective_user
    if not user:
        return

    users = load_users()
    key = str(user.id)

    existing = users.get(key, {})
    users[key] = {
        "user_id": user.id,
        "username": user.username or "",
        "full_name": user.full_name or "",
        "first_seen": existing.get("first_seen", now_str()),
        "last_seen": now_str(),
        "last_seen_ts": time.time(),
        "language": normalize_language(language or existing.get("language")),
    }
    save_users(users)


def load_subscribers() -> list[dict]:
    try:
        data = json.loads(Path(SUBSCRIBERS_FILE).read_text(encoding="utf-8"))
    except Exception:
        return []

    result = []
    if isinstance(data, list):
        for item in data:
            if isinstance(item, int):
                result.append({
                    "chat_id": item,
                    "chat_type": "private",
                    "title": "",
                    "username": "",
                    "full_name": "",
                    "first_seen": "",
                    "last_seen": "",
                })
            elif isinstance(item, dict) and "chat_id" in item:
                result.append({
                    "chat_id": int(item.get("chat_id")),
                    "chat_type": item.get("chat_type", "private"),
                    "title": item.get("title", ""),
                    "username": item.get("username", ""),
                    "full_name": item.get("full_name", ""),
                    "first_seen": item.get("first_seen", ""),
                    "last_seen": item.get("last_seen", ""),
                })
    return result


def save_subscribers(subscribers: list[dict]):
    unique = {}
    for item in subscribers:
        chat_id = int(item["chat_id"])
        unique[chat_id] = {
            "chat_id": chat_id,
            "chat_type": item.get("chat_type", "private"),
            "title": item.get("title", ""),
            "username": item.get("username", ""),
            "full_name": item.get("full_name", ""),
            "first_seen": item.get("first_seen", ""),
            "last_seen": item.get("last_seen", ""),
        }

    data = list(unique.values())
    data.sort(key=lambda x: x["chat_id"])

    Path(SUBSCRIBERS_FILE).write_text(
        json.dumps(data, ensure_ascii=False, indent=2),
        encoding="utf-8"
    )


def remember_chat_subscription(update: Update):
    chat = update.effective_chat
    if not chat:
        return

    subscribers = load_subscribers()
    chat_id = int(chat.id)
    now = now_str()

    existing = None
    for item in subscribers:
        if int(item["chat_id"]) == chat_id:
            existing = item
            break

    if not existing:
        existing = {
            "chat_id": chat_id,
            "chat_type": chat.type,
            "title": getattr(chat, "title", "") or "",
            "username": "",
            "full_name": "",
            "first_seen": now,
            "last_seen": now,
        }
        subscribers.append(existing)

    existing["chat_type"] = chat.type
    existing["title"] = getattr(chat, "title", "") or ""
    existing["last_seen"] = now

    if chat.type == "private" and update.effective_user:
        existing["username"] = update.effective_user.username or ""
        existing["full_name"] = update.effective_user.full_name or ""

    save_subscribers(subscribers)


def clean_proxy_line(line: str) -> str:
    line = re.sub(r"\s+#.*$", "", line.strip())
    return line.strip()


def detect_proxy_type_from_url(parsed) -> str:
    target = f"{parsed.netloc}{parsed.path}".lower()
    if "socks" in target:
        return "socks5"
    if "proxy" in target:
        return "mtproto"
    return "unknown"


def parse_proxy_line(line: str):
    line = clean_proxy_line(line)
    if not line:
        return None

    if (
        line.startswith("tg://")
        or line.startswith("https://t.me/")
        or line.startswith("http://t.me/")
    ):
        try:
            parsed = urlparse(line)
            qs = parse_qs(parsed.query)

            server = qs.get("server", [None])[0]
            port = qs.get("port", [None])[0]
            proxy_type = detect_proxy_type_from_url(parsed)

            if server and port:
                return line, server, int(port), proxy_type
        except Exception:
            return None

    if "@" in line:
        hostport = line.rsplit("@", 1)[1]
    else:
        hostport = line

    if ":" not in hostport:
        return None

    try:
        if "@" in line:
            host, port_text = hostport.rsplit(":", 1)
            return line, host.strip("[]"), int(port_text), "socks5"

        parts = hostport.split(":")

        if len(parts) == 2:
            host, port_text = parts
            proxy_type = "unknown"
        elif len(parts) == 3:
            host, port_text, _secret = parts
            proxy_type = "mtproto"
        elif len(parts) >= 4:
            host, port_text = parts[0], parts[1]
            proxy_type = "socks5"
        else:
            return None

        return line, host.strip("[]"), int(port_text), proxy_type
    except Exception:
        return None


async def safe_answer_callback(query, text: str | None = None, show_alert: bool = False):
    try:
        await query.answer(text=text, show_alert=show_alert)
    except Exception:
        pass



def load_all_proxy_originals_from_input() -> list[str]:
    return [item[0] for item in parse_unique_proxy_lines(load_input_proxy_lines())]


def load_input_proxy_lines() -> list[str]:
    with open(INPUT_FILE, "r", encoding="utf-8") as f:
        return [line.rstrip("\n") for line in f]


def parse_unique_proxy_lines(lines: list[str]) -> list[tuple[str, str, int, str]]:
    parsed_items = []
    seen = set()

    for line in lines:
        parsed = parse_proxy_line(line)
        if parsed is None:
            continue

        original = parsed[0]
        if original in seen:
            continue

        seen.add(original)
        parsed_items.append(parsed)

    return parsed_items


def build_check_result(alive: list, dead: list, total: int) -> dict:
    alive.sort(key=lambda x: x[4])
    return {
        "total": total,
        "alive_count": len(alive),
        "dead_count": len(dead),
        "socks_alive": sum(1 for x in alive if x[1] == "socks5"),
        "mtproto_alive": sum(1 for x in alive if x[1] == "mtproto"),
        "unknown_alive": sum(1 for x in alive if x[1] == "unknown"),
        "alive": alive,
        "dead": dead,
    }


def normalize_proxy_type(value) -> str:
    value = str(value or "").strip().lower()
    if value in {"socks5", "mtproto", "unknown"}:
        return value
    return "unknown"


def parse_latency_ms(value) -> float:
    try:
        return round(float(value), 1)
    except Exception as exc:
        raise RuntimeError(f"API returned invalid latency value: {value!r}") from exc


def normalize_api_proxy_item(item, forced_alive: bool | None = None):
    if not isinstance(item, dict):
        raise RuntimeError("API returned proxy item in invalid format.")

    original = clean_proxy_line(str(item.get("original", "")).strip())
    if not original:
        raise RuntimeError("API returned proxy item without original.")

    parsed = parse_proxy_line(original)
    fallback_ip = parsed[1] if parsed else None
    fallback_port = parsed[2] if parsed else None
    fallback_type = parsed[3] if parsed else "unknown"

    proxy_type = normalize_proxy_type(item.get("proxy_type") or fallback_type)
    ip = str(item.get("ip") or fallback_ip or "").strip()
    if not ip:
        raise RuntimeError(f"API did not return ip for proxy: {original}")

    try:
        port = int(item.get("port", fallback_port))
    except Exception as exc:
        raise RuntimeError(f"API returned invalid port for proxy: {original}") from exc

    is_alive = forced_alive
    if is_alive is None:
        if "alive" in item:
            is_alive = bool(item["alive"])
        else:
            status = str(item.get("status", "")).strip().lower()
            if status in {"alive", "ok", "up", "live"}:
                is_alive = True
            elif status in {"dead", "down", "failed", "fail"}:
                is_alive = False
            else:
                raise RuntimeError(f"API did not return proxy status for: {original}")

    latency_ms = None
    if is_alive:
        latency_ms = parse_latency_ms(
            item.get("latency_ms", item.get("latency", item.get("ms")))
        )

    return original, proxy_type, ip, port, is_alive, latency_ms


def normalize_api_result(data: dict, expected_originals: list[str]) -> dict:
    if not isinstance(data, dict):
        raise RuntimeError("API returned invalid JSON root.")

    if data.get("ok") is False:
        raise RuntimeError(str(data.get("error") or "API returned ok=false."))

    alive = []
    dead = []
    seen = set()

    def add_entry(entry, forced_alive: bool | None = None):
        original, proxy_type, ip, port, is_alive, latency_ms = normalize_api_proxy_item(
            entry,
            forced_alive=forced_alive,
        )

        if original in seen:
            raise RuntimeError(f"API returned duplicate proxy result: {original}")

        seen.add(original)
        if is_alive:
            alive.append((original, proxy_type, ip, port, latency_ms))
        else:
            dead.append((original, proxy_type, ip, port))

    if isinstance(data.get("alive"), list) or isinstance(data.get("dead"), list):
        for entry in data.get("alive", []):
            add_entry(entry, forced_alive=True)
        for entry in data.get("dead", []):
            add_entry(entry, forced_alive=False)
    elif isinstance(data.get("results"), list):
        for entry in data["results"]:
            add_entry(entry, forced_alive=None)
    else:
        raise RuntimeError("API response must contain alive/dead arrays or results array.")

    expected_set = set(expected_originals)
    missing = expected_set - seen
    unexpected = seen - expected_set

    if missing:
        raise RuntimeError(f"API returned incomplete result: missing {len(missing)} proxies.")
    if unexpected:
        raise RuntimeError(f"API returned unexpected result entries: {len(unexpected)}.")

    return build_check_result(alive, dead, len(expected_originals))


def post_json_request(url: str, payload: dict, token: str, timeout_seconds: int) -> dict:
    data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    headers = {
        "Content-Type": "application/json; charset=utf-8",
        "Accept": "application/json",
    }

    if token:
        headers["Authorization"] = f"Bearer {token}"

    request = urllib_request.Request(url, data=data, headers=headers, method="POST")

    try:
        with urllib_request.urlopen(request, timeout=timeout_seconds) as response:
            body = response.read().decode("utf-8", errors="replace")
    except urllib_error.HTTPError as exc:
        body = exc.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"API HTTP {exc.code}: {body[:300] or exc.reason}") from exc
    except urllib_error.URLError as exc:
        raise RuntimeError(f"API connection error: {exc.reason}") from exc
    except Exception as exc:
        raise RuntimeError(f"API request failed: {exc}") from exc

    try:
        return json.loads(body)
    except Exception as exc:
        raise RuntimeError("API returned invalid JSON body.") from exc


async def check_tcp(ip: str, port: int, timeout: int = TIMEOUT):
    started = time.perf_counter()
    try:
        conn = asyncio.open_connection(ip, port)
        _, writer = await asyncio.wait_for(conn, timeout=timeout)
        elapsed_ms = round((time.perf_counter() - started) * 1000, 1)

        writer.close()
        try:
            await writer.wait_closed()
        except Exception:
            pass

        return True, elapsed_ms
    except Exception:
        return False, None


async def worker(queue: asyncio.Queue, alive: list, dead: list, sem: asyncio.Semaphore):
    while True:
        item = await queue.get()
        if item is None:
            queue.task_done()
            return

        original, ip, port, proxy_type = item

        async with sem:
            ok, ms = await check_tcp(ip, port)

        if ok:
            alive.append((original, proxy_type, ip, port, ms))
        else:
            dead.append((original, proxy_type, ip, port))

        queue.task_done()


async def run_local_proxy_check(parsed_items: list[tuple[str, str, int, str]]):
    if not parsed_items:
        return build_check_result([], [], 0)

    queue = asyncio.Queue()
    sem = asyncio.Semaphore(CONCURRENCY)

    alive = []
    dead = []

    for item in parsed_items:
        await queue.put(item)

    workers = [
        asyncio.create_task(worker(queue, alive, dead, sem))
        for _ in range(min(CONCURRENCY, max(1, len(parsed_items))))
    ]

    await queue.join()

    for _ in workers:
        await queue.put(None)

    await asyncio.gather(*workers)
    return build_check_result(alive, dead, len(parsed_items))


async def run_remote_proxy_check(parsed_items: list[tuple[str, str, int, str]]):
    config = load_api_config()
    if not config["enabled"]:
        raise RuntimeError("External checker API is disabled.")
    if not config["base_url"]:
        raise RuntimeError("proxy_check_api.json: base_url is empty.")
    if not config["token"]:
        raise RuntimeError("proxy_check_api.json: token is empty.")

    payload = {
        "proxies": [item[0] for item in parsed_items],
        "timeout": config["check_timeout_seconds"],
        "timeout_seconds": config["check_timeout_seconds"],
        "concurrency": config["check_concurrency"],
    }
    url = f"{config['base_url']}/v1/check"

    started = time.perf_counter()
    data = await asyncio.to_thread(
        post_json_request,
        url,
        payload,
        config["token"],
        config["request_timeout_seconds"],
    )
    result = normalize_api_result(data, payload["proxies"])
    elapsed_ms = round((time.perf_counter() - started) * 1000, 1)
    print(
        f"[CHECK API] {len(parsed_items)} proxies checked via {url} "
        f"in {elapsed_ms} ms"
    )
    return result


async def run_proxy_check_for_lines(proxy_lines: list[str]):
    parsed_items = parse_unique_proxy_lines(proxy_lines)
    if not parsed_items:
        return build_check_result([], [], 0)

    config = load_api_config()
    if config["enabled"]:
        try:
            return await run_remote_proxy_check(parsed_items)
        except Exception as exc:
            if not config["fallback_to_local"]:
                raise
            print(f"[CHECK API FALLBACK] {exc}")

    return await run_local_proxy_check(parsed_items)


async def run_proxy_check():
    result = await run_proxy_check_for_lines(load_input_proxy_lines())
    global last_result
    last_result = result | {"checked": True}
    return result


def get_add_url(original: str, proxy_type: str):
    if original.startswith(("https://t.me/", "http://t.me/", "tg://")):
        return original
    return None


def get_fastest_connect_item():
    for item in last_result["alive"]:
        add_url = get_add_url(item[0], item[1])
        if add_url:
            return item, add_url
    return None, None


def format_alive_proxy(item, lang: str, index: int | None = None):
    original, proxy_type, ip, port, ms = item
    prefix = f"{index}. " if index is not None else ""
    addable = tr(lang, "yes") if get_add_url(original, proxy_type) else tr(lang, "no")
    return (
        f"{prefix}[{proxy_type}] {ip}:{port}\n"
        f"{tr(lang, 'latency', ms=ms)}\n"
        f"{tr(lang, 'add_to_telegram', value=addable)}"
    )


def format_dead_proxy(item, index: int | None = None):
    original, proxy_type, ip, port = item
    prefix = f"{index}. " if index is not None else ""
    return f"{prefix}[{proxy_type}] {ip}:{port}"


def paginate(items, page: int, page_size: int = PAGE_SIZE):
    start = page * page_size
    end = start + page_size
    return items[start:end], len(items)


def build_main_menu(for_owner: bool, lang: str, show_atlas: bool = False):
    _fastest_item, quick_url = get_fastest_connect_item()

    quick_connect_button = (
        InlineKeyboardButton(tr(lang, "button_quick_connect"), url=quick_url)
        if quick_url
        else InlineKeyboardButton(tr(lang, "button_quick_connect"), callback_data="action:quickconnect")
    )

    keyboard = [
        [
            quick_connect_button,
        ],
        [
            InlineKeyboardButton(tr(lang, "button_my_pack"), callback_data="mypack:menu"),
        ],
        [
            InlineKeyboardButton(tr(lang, "button_alive_socks"), callback_data="view:socks:0"),
            InlineKeyboardButton(tr(lang, "button_alive_mtproto"), callback_data="view:mtproto:0"),
        ],
        [
            InlineKeyboardButton(tr(lang, "button_pack_all"), callback_data="pack:all"),
        ],
        [
            InlineKeyboardButton(tr(lang, "button_check_now"), callback_data="action:check"),
        ],
        [
            InlineKeyboardButton(tr(lang, "button_whitelist"), callback_data="view:whitelist:0"),
        ],
        [
            InlineKeyboardButton(tr(lang, "button_language"), callback_data="action:language"),
        ],
    ]

    if show_atlas:
        keyboard.insert(1, [InlineKeyboardButton(tr(lang, "button_atlas"), callback_data="atlas:menu")])

    if for_owner:
        keyboard.append([
            InlineKeyboardButton(tr(lang, "button_reload_base"), callback_data="action:reloadbase"),
            InlineKeyboardButton(tr(lang, "button_users"), callback_data="owner:users:0"),
        ])

    return InlineKeyboardMarkup(keyboard)

def build_language_menu(lang: str, for_owner: bool):
    keyboard = []
    for code in SUPPORTED_LANGUAGES:
        prefix = "✅ " if code == lang else ""
        keyboard.append([
            InlineKeyboardButton(f"{prefix}{LANGUAGE_LABELS[code]}", callback_data=f"lang:set:{code}")
        ])
    keyboard.append([InlineKeyboardButton(tr(lang, "button_menu"), callback_data="action:menu")])
    return InlineKeyboardMarkup(keyboard)


def build_my_pack_menu(pack: dict, lang: str):
    items = pack.get("items", [])
    whitelist = set(load_whitelist_proxies())

    whitelist_count = sum(1 for x in items if x.get("proxy") in whitelist)

    keyboard = [
        [InlineKeyboardButton(tr(lang, "button_fastest"), callback_data="mypack:best")],
        [InlineKeyboardButton(tr(lang, "button_my_whitelist", count=whitelist_count), callback_data="mypack:view:white:0")],
        [InlineKeyboardButton(tr(lang, "button_my_all_servers", count=len(items)), callback_data="mypack:view:all:0")],
        [InlineKeyboardButton(tr(lang, "button_refresh_pack"), callback_data="mypack:refresh")],
        [InlineKeyboardButton(tr(lang, "button_remove_dead"), callback_data="mypack:remove_dead")],
        [InlineKeyboardButton(tr(lang, "button_clear_pack"), callback_data="mypack:clear:confirm")],
        [InlineKeyboardButton(tr(lang, "button_menu"), callback_data="action:menu")],
    ]
    return InlineKeyboardMarkup(keyboard)

async def send_my_pack_menu(query, update: Update):
    lang = get_update_language(update)
    pack = get_user_pack(update.effective_user.id)
    text = (
        f"{tr(lang, 'my_pack_title')}\n\n"
        f"{tr(lang, 'servers_in_pack', count=len(pack.get('items', [])))}\n"
        f"{tr(lang, 'updated', value=pack.get('updated_at') or '-')}"
    )
    await query.edit_message_text(text, reply_markup=build_my_pack_menu(pack, lang))


async def send_language_menu(query, update: Update, for_owner: bool):
    lang = get_update_language(update)
    text = (
        f"{tr(lang, 'language_title')}\n\n"
        f"{tr(lang, 'language_current', value=LANGUAGE_LABELS[lang])}"
    )
    await query.edit_message_text(text, reply_markup=build_language_menu(lang, for_owner))


async def handle_quick_connect_action(query, update: Update, for_owner: bool):
    lang = get_update_language(update)
    atlas_access = has_atlas_access(update)
    fastest_item, add_url = get_fastest_connect_item()

    if not fastest_item or not add_url:
        text = (
            f"{tr(lang, 'quick_connect_title')}\n\n"
            f"{tr(lang, 'quick_connect_missing')}"
        )
        await query.edit_message_text(text, reply_markup=build_main_menu(for_owner, lang, atlas_access))
        return

    keyboard = InlineKeyboardMarkup([
        [InlineKeyboardButton(tr(lang, "button_quick_connect"), url=add_url)],
        [InlineKeyboardButton(tr(lang, "button_menu"), callback_data="action:menu")],
    ])
    text = f"{tr(lang, 'quick_connect_title')}\n\n{format_alive_proxy(fastest_item, lang, 1)}"
    await query.edit_message_text(text, reply_markup=keyboard)


def telegram_user_info(update: Update) -> dict:
    user = update.effective_user
    if not user:
        return {}

    return {
        "user_id": user.id,
        "username": user.username or "",
        "first_name": user.first_name or "",
        "last_name": user.last_name or "",
        "full_name": user.full_name or "",
        "language_code": user.language_code or "",
        "is_bot": bool(user.is_bot),
        "is_premium": bool(getattr(user, "is_premium", False)),
    }


def build_atlas_migration_menu(lang: str):
    return InlineKeyboardMarkup([
        [InlineKeyboardButton("🔑 Получить ключ", callback_data="migration:key")],
        [InlineKeyboardButton("🤖 Открыть @ElyAxisBot", url=ELYAXIS_BOT_URL)],
        [InlineKeyboardButton(tr(lang, "button_menu"), callback_data="action:menu")],
    ])


async def send_atlas_migration_panel(query, update: Update):
    lang = get_update_language(update)
    text = (
        "Привет! Этот бот больше не используется для вопросов, связанных с VPN.\n\n"
        "Пожалуйста, используй нового бота. Для этого нажми «Получить ключ».\n\n"
        "Ключ одноразовый, и сгенерировать новый невозможно. "
        "Активировать ключ может только один человек. "
        "Такой ключ предназначен для пользователей, которые мигрируют с этого бота на новый.\n\n"
        "Бот: @ElyAxisBot"
    )
    await query.edit_message_text(text, reply_markup=build_atlas_migration_menu(lang))


async def send_atlas_migration_key_panel(query, update: Update):
    lang = get_update_language(update)
    result = get_or_create_migration_key(telegram_user_info(update))
    key = result["key"]
    record = result["record"]

    keyboard = InlineKeyboardMarkup([
        [InlineKeyboardButton("🤖 Открыть @ElyAxisBot", url=ELYAXIS_BOT_URL)],
        [InlineKeyboardButton(tr(lang, "button_back"), callback_data="atlas:menu")],
        [InlineKeyboardButton(tr(lang, "button_menu"), callback_data="action:menu")],
    ])

    if record.get("used"):
        used_by = record.get("used_by_username") or record.get("used_by_full_name") or record.get("used_by_user_id") or "пользователем"
        text = (
            "Ключ уже был активирован.\n\n"
            f"Активировал: {used_by}\n"
            f"Время активации: {record.get('used_at') or '-'}\n\n"
            "Новый ключ для этого пользователя сгенерировать невозможно."
        )
        await query.edit_message_text(text, reply_markup=keyboard)
        return

    text = (
        "Твой одноразовый ключ для ElyAxis:\n\n"
        f"<code>{key}</code>\n\n"
        "Открой @ElyAxisBot и отправь этот ключ сообщением. "
        "После активации этот ключ больше никому не подойдёт."
    )
    await query.edit_message_text(text, reply_markup=keyboard, parse_mode="HTML")


def build_atlas_home_menu(lang: str):
    return InlineKeyboardMarkup([
        [InlineKeyboardButton(tr(lang, "button_atlas_soft"), callback_data="atlas:soft")],
        [InlineKeyboardButton(tr(lang, "button_atlas_vless"), callback_data="atlas:vless")],
        [InlineKeyboardButton(tr(lang, "button_atlas_guides"), callback_data="atlas:guides")],
        [InlineKeyboardButton(tr(lang, "button_menu"), callback_data="action:menu")],
    ])


def build_atlas_soft_menu(lang: str):
    keyboard = [
        [InlineKeyboardButton(tr(lang, "button_atlas_soft_shadowsocks"), callback_data="atlas:soft:shadowsocks")],
        [InlineKeyboardButton(tr(lang, "button_atlas_soft_happ_ios_global"), url=ATLAS_SOFT_LINKS["happ_ios_global"])],
        [InlineKeyboardButton(tr(lang, "button_atlas_soft_happ_ios_ru"), url=ATLAS_SOFT_LINKS["happ_ios_ru"])],
        [InlineKeyboardButton(tr(lang, "button_atlas_soft_happ_android_apk"), url=ATLAS_SOFT_LINKS["happ_android_apk"])],
        [InlineKeyboardButton(tr(lang, "button_atlas_soft_happ_windows"), url=ATLAS_SOFT_LINKS["happ_windows"])],
        [InlineKeyboardButton(tr(lang, "button_atlas_soft_elyproxy_windows"), callback_data="atlas:soft:elyproxy")],
        [InlineKeyboardButton(tr(lang, "button_back"), callback_data="atlas:menu")],
        [InlineKeyboardButton(tr(lang, "button_menu"), callback_data="action:menu")],
    ]
    return InlineKeyboardMarkup(keyboard)


def build_atlas_shadowsocks_browser_menu(lang: str):
    return InlineKeyboardMarkup([
        [InlineKeyboardButton(tr(lang, "button_browser_firefox"), url=ATLAS_SHADOWSOCKS_BROWSER_LINKS["firefox"])],
        [InlineKeyboardButton(tr(lang, "button_browser_chrome"), url=ATLAS_SHADOWSOCKS_BROWSER_LINKS["chrome"])],
        [InlineKeyboardButton(tr(lang, "button_back"), callback_data="atlas:soft")],
        [InlineKeyboardButton(tr(lang, "button_menu"), callback_data="action:menu")],
    ])


def build_atlas_elyproxy_menu(lang: str):
    return InlineKeyboardMarkup([
        [InlineKeyboardButton(tr(lang, "button_atlas_soft_elyproxy_internal"), url=ATLAS_ELYPROXY_LINKS["internal"])],
        [InlineKeyboardButton(tr(lang, "button_atlas_soft_elyproxy_release"), url=ATLAS_ELYPROXY_LINKS["release"])],
        [InlineKeyboardButton(tr(lang, "button_atlas_soft_elyproxy_old"), url=ATLAS_ELYPROXY_LINKS["old"])],
        [InlineKeyboardButton(tr(lang, "button_atlas_soft_elyproxy_repo"), url=ATLAS_ELYPROXY_LINKS["repo"])],
        [InlineKeyboardButton(tr(lang, "button_back"), callback_data="atlas:soft")],
        [InlineKeyboardButton(tr(lang, "button_menu"), callback_data="action:menu")],
    ])


def build_atlas_vless_menu(lang: str):
    return InlineKeyboardMarkup([
        [InlineKeyboardButton(tr(lang, "button_atlas_vless_normal"), callback_data="atlas:vless:normal")],
        [InlineKeyboardButton(tr(lang, "button_atlas_vless_whitelist"), callback_data="atlas:vless:whitelist")],
        [InlineKeyboardButton(tr(lang, "button_back"), callback_data="atlas:menu")],
        [InlineKeyboardButton(tr(lang, "button_menu"), callback_data="action:menu")],
    ])


def build_atlas_guides_menu(lang: str):
    return InlineKeyboardMarkup([
        [InlineKeyboardButton(tr(lang, "button_atlas_guide_instruction"), callback_data="atlas:guide:instruction")],
        [
            InlineKeyboardButton(tr(lang, "button_atlas_guide_text_data"), callback_data="atlas:guide:textdata"),
            InlineKeyboardButton(tr(lang, "button_atlas_guide_test_url"), callback_data="atlas:guide:testurl"),
        ],
        [InlineKeyboardButton(tr(lang, "button_atlas_guide_routing_ru"), callback_data="atlas:guide:routingru")],
        [InlineKeyboardButton(tr(lang, "button_back"), callback_data="atlas:menu")],
        [InlineKeyboardButton(tr(lang, "button_menu"), callback_data="action:menu")],
    ])


async def send_atlas_panel(query, update: Update):
    lang = get_update_language(update)
    text = "\n".join([tr(lang, "atlas_title"), "", tr(lang, "atlas_intro")])
    await query.edit_message_text(text, reply_markup=build_atlas_home_menu(lang))


async def send_atlas_soft_panel(query, update: Update):
    lang = get_update_language(update)
    text = "\n".join([tr(lang, "atlas_title"), "", tr(lang, "atlas_soft_title")])
    await query.edit_message_text(text, reply_markup=build_atlas_soft_menu(lang))


async def send_atlas_shadowsocks_browser_panel(query, update: Update):
    lang = get_update_language(update)
    text = "\n".join([tr(lang, "atlas_title"), "", tr(lang, "atlas_soft_shadowsocks_title")])
    await query.edit_message_text(text, reply_markup=build_atlas_shadowsocks_browser_menu(lang))


async def send_atlas_elyproxy_panel(query, update: Update):
    lang = get_update_language(update)
    text = "\n".join([tr(lang, "atlas_title"), "", tr(lang, "atlas_soft_elyproxy_title")])
    await query.edit_message_text(text, reply_markup=build_atlas_elyproxy_menu(lang))


async def send_atlas_vless_panel(query, update: Update):
    lang = get_update_language(update)
    keyboard = InlineKeyboardMarkup([
        [InlineKeyboardButton(tr(lang, "button_back"), callback_data="atlas:menu")],
        [InlineKeyboardButton(tr(lang, "button_menu"), callback_data="action:menu")],
    ])
    text = "\n".join([
        tr(lang, "atlas_title"),
        "",
        tr(lang, "atlas_vless_title"),
        "",
        "BlancVPN:",
        f"<code>{ATLAS_KEY_URL}</code>",
        "",
        "LibertyVPN:",
        f"<code>{ATLAS_LIBERTY_KEY_URL}</code>",
        "",
        tr(lang, "atlas_vless_copy_hint"),
    ])
    await query.edit_message_text(text, reply_markup=keyboard, parse_mode="HTML")


async def send_atlas_vless_text(query, update: Update, kind: str):
    lang = get_update_language(update)
    text_key = "button_atlas_vless_normal" if kind == "normal" else "button_atlas_vless_whitelist"
    url = ATLAS_VLESS_LINKS["normal" if kind == "normal" else "whitelist"]
    keyboard = InlineKeyboardMarkup([
        [InlineKeyboardButton(tr(lang, "button_back"), callback_data="atlas:vless")],
        [InlineKeyboardButton(tr(lang, "button_menu"), callback_data="action:menu")],
    ])
    text = "\n".join([tr(lang, "atlas_title"), "", tr(lang, text_key), "", url])
    await query.edit_message_text(text, reply_markup=keyboard)


async def send_atlas_guides_panel(query, update: Update):
    lang = get_update_language(update)
    text = "\n".join([tr(lang, "atlas_title"), "", tr(lang, "atlas_guides_title")])
    await query.edit_message_text(text, reply_markup=build_atlas_guides_menu(lang))


async def send_atlas_instruction_panel(query, update: Update):
    lang = get_update_language(update)
    text = "\n".join([
        tr(lang, "atlas_title"),
        "",
        tr(lang, "atlas_guide_instruction_title"),
        "",
        f"🔗 <code>{ATLAS_GUIDE_INSTRUCTION_URL}</code>",
        "",
        tr(lang, "atlas_copy_hint"),
    ])
    keyboard = InlineKeyboardMarkup([
        [InlineKeyboardButton(tr(lang, "button_atlas_open_instruction"), url=ATLAS_GUIDE_INSTRUCTION_URL)],
        [InlineKeyboardButton(tr(lang, "button_back"), callback_data="atlas:guides")],
        [InlineKeyboardButton(tr(lang, "button_menu"), callback_data="action:menu")],
    ])
    await query.edit_message_text(text, reply_markup=keyboard, parse_mode="HTML")


async def send_atlas_text_data_panel(query, update: Update):
    lang = get_update_language(update)
    text = "\n".join([
        tr(lang, "atlas_title"),
        "",
        tr(lang, "atlas_guide_text_data_title"),
        "",
        f"🧩 DNS IP для резольвинга сервера: <code>{ATLAS_DNS_IP}</code>",
        f"🌐 DNS домен для резольвинга сервера: <code>{ATLAS_DNS_DOMAIN}</code>",
        "",
        tr(lang, "atlas_copy_hint"),
    ])
    keyboard = InlineKeyboardMarkup([
        [InlineKeyboardButton(tr(lang, "button_back"), callback_data="atlas:guides")],
        [InlineKeyboardButton(tr(lang, "button_menu"), callback_data="action:menu")],
    ])
    await query.edit_message_text(text, reply_markup=keyboard, parse_mode="HTML")


async def send_atlas_test_url_panel(query, update: Update):
    lang = get_update_language(update)
    text = "\n".join([
        tr(lang, "atlas_title"),
        "",
        tr(lang, "atlas_guide_test_url_title"),
        "",
        f"1) <code>{ATLAS_TEST_URLS[0]}</code>",
        f"2) <code>{ATLAS_TEST_URLS[1]}</code>",
        "",
        tr(lang, "atlas_copy_hint"),
    ])
    keyboard = InlineKeyboardMarkup([
        [InlineKeyboardButton(tr(lang, "button_back"), callback_data="atlas:guides")],
        [InlineKeyboardButton(tr(lang, "button_menu"), callback_data="action:menu")],
    ])
    await query.edit_message_text(text, reply_markup=keyboard, parse_mode="HTML")


async def send_atlas_routing_ru_panel(query, update: Update):
    lang = get_update_language(update)
    text = "\n".join([
        tr(lang, "atlas_title"),
        "",
        tr(lang, "atlas_guide_routing_ru_title"),
        "",
        f"🧾 <code>{ATLAS_ROUTING_RU_URL}</code>",
        "",
        tr(lang, "atlas_copy_hint"),
    ])
    keyboard = InlineKeyboardMarkup([
        [InlineKeyboardButton(tr(lang, "button_back"), callback_data="atlas:guides")],
        [InlineKeyboardButton(tr(lang, "button_menu"), callback_data="action:menu")],
    ])
    await query.edit_message_text(text, reply_markup=keyboard, parse_mode="HTML")


async def send_atlas_notification(app: Application):
    sent = 0
    failed = 0

    for user_id in sorted(ATLAS_USER_IDS):
        lang = get_user_language(user_id)
        try:
            await app.bot.send_message(chat_id=user_id, text=tr(lang, "atlas_notification_text"))
            sent += 1
        except Exception as e:
            failed += 1
            print(f"[ATLAS NOTIFY ERROR] {user_id}: {e}")

    return sent, failed


async def send_my_pack_page(query, update: Update, page: int, only_whitelist: bool = False):
    lang = get_update_language(update)
    pack = get_user_pack(update.effective_user.id)
    items = pack.get("items", [])

    if only_whitelist:
        wl = set(load_whitelist_proxies())
        items = [x for x in items if x.get("proxy") in wl]
        title = tr(lang, "my_pack_whitelist_title")
    else:
        title = tr(lang, "my_pack_all_title")

    page_items, total = paginate(items, page)

    if not items:
        await query.edit_message_text(
            tr(lang, "pack_empty", title=title),
            reply_markup=build_my_pack_menu(pack, lang)
        )
        return

    lines = [title, ""]
    keyboard = []

    for idx, item in enumerate(page_items, start=page * PAGE_SIZE + 1):
        lines.append(format_my_pack_item(item, lang, idx))
        lines.append("")

        parsed = parse_proxy_line(item["proxy"])
        proxy_type = parsed[3] if parsed else "unknown"
        add_url = get_add_url(item["proxy"], proxy_type)
        row = []
        if add_url:
            row.append(InlineKeyboardButton(tr(lang, "button_connect", index=idx), url=add_url))
        keyboard.append(row or [InlineKeyboardButton(tr(lang, "button_noop", index=idx), callback_data="noop")])

    nav = []
    kind = "white" if only_whitelist else "all"
    if page > 0:
        nav.append(InlineKeyboardButton(tr(lang, "button_back"), callback_data=f"mypack:view:{kind}:{page - 1}"))
    if (page + 1) * PAGE_SIZE < total:
        nav.append(InlineKeyboardButton(tr(lang, "button_next"), callback_data=f"mypack:view:{kind}:{page + 1}"))
    if nav:
        keyboard.append(nav)

    keyboard.append([InlineKeyboardButton(tr(lang, "button_back_to_pack"), callback_data="mypack:menu")])

    await query.edit_message_text(
        "\n".join(lines).strip(),
        reply_markup=InlineKeyboardMarkup(keyboard)
    )

async def send_fastest_from_my_pack(query, update: Update):
    lang = get_update_language(update)
    pack = await refresh_user_pack(update)
    items = [x for x in pack.get("items", []) if x.get("last_status") == "alive" and x.get("last_ms") is not None]

    if not items:
        await query.edit_message_text(
            f"{tr(lang, 'my_pack_title')}\n\n{tr(lang, 'no_alive_in_pack')}",
            reply_markup=build_my_pack_menu(pack, lang)
        )
        return

    items.sort(key=lambda x: x["last_ms"])
    best = items[0]

    parsed = parse_proxy_line(best["proxy"])
    proxy_type = parsed[3] if parsed else "unknown"
    add_url = get_add_url(best["proxy"], proxy_type)

    keyboard = []
    if add_url:
        keyboard.append([InlineKeyboardButton(tr(lang, "button_connect_fastest"), url=add_url)])
    keyboard.append([InlineKeyboardButton(tr(lang, "button_back_to_pack"), callback_data="mypack:menu")])

    await query.edit_message_text(
        f"{tr(lang, 'fastest_pack_title')}\n\n{format_my_pack_item(best, lang, 1)}",
        reply_markup=InlineKeyboardMarkup(keyboard)
    )

async def refresh_my_pack_view(query, update: Update):
    lang = get_update_language(update)
    pack = await refresh_user_pack(update)

    alive_count = sum(1 for x in pack["items"] if x.get("last_status") == "alive")
    dead_count = sum(1 for x in pack["items"] if x.get("last_status") == "dead")

    text = (
        f"{tr(lang, 'pack_refreshed')}\n\n"
        f"{tr(lang, 'total_in_pack', count=len(pack['items']))}\n"
        f"{tr(lang, 'alive_count', count=alive_count)}\n"
        f"{tr(lang, 'dead_count', count=dead_count)}\n"
        f"{tr(lang, 'updated', value=pack.get('updated_at') or '-')}"
    )

    await query.edit_message_text(text, reply_markup=build_my_pack_menu(pack, lang))

async def remove_dead_from_my_pack(query, update: Update):
    lang = get_update_language(update)
    pack = await refresh_user_pack(update)
    items = pack.get("items", [])

    alive_items = [x for x in items if x.get("last_status") != "dead"]
    removed = len(items) - len(alive_items)

    pack["items"] = alive_items
    save_user_pack_from_update(update, pack)

    await query.edit_message_text(
        tr(lang, "removed_dead", count=removed),
        reply_markup=build_my_pack_menu(pack, lang)
    )

async def clear_my_pack_confirm(query, update: Update):
    lang = get_update_language(update)
    keyboard = InlineKeyboardMarkup([
        [InlineKeyboardButton(tr(lang, "button_confirm_clear"), callback_data="mypack:clear:do")],
        [InlineKeyboardButton(tr(lang, "button_cancel"), callback_data="mypack:menu")],
    ])
    await query.edit_message_text(tr(lang, "clear_pack_confirm"), reply_markup=keyboard)


async def clear_my_pack_do(query, update: Update):
    lang = get_update_language(update)
    pack = get_user_pack(update.effective_user.id)
    pack["items"] = []
    save_user_pack_from_update(update, pack)

    await query.edit_message_text(
        tr(lang, "pack_cleared"),
        reply_markup=build_my_pack_menu(pack, lang)
    )

def format_whitelist_proxy(item, lang: str, index: int | None = None):
    """
    item = (original, proxy_type, ip, port, status, ms)
    status: alive / dead / unknown
    """
    original, proxy_type, ip, port, status, ms = item
    prefix = f"{index}. " if index is not None else ""

    if status == "alive":
        icon = "✅"
        status_text = tr(lang, "status_alive", ms=ms)
    elif status == "dead":
        icon = "☠️"
        status_text = tr(lang, "status_dead")
    else:
        icon = "❓"
        status_text = tr(lang, "status_unknown_short")

    addable = tr(lang, "yes") if get_add_url(original, proxy_type) else tr(lang, "no")

    lines = [
        f"{prefix}{icon} [{proxy_type}] {ip}:{port}",
        tr(lang, "status_label", value=status_text),
        tr(lang, "add_to_telegram", value=addable),
    ]
    return "\n".join(lines)

def get_whitelist_items():
    whitelist = load_whitelist_proxies()

    alive_map = {item[0]: item for item in last_result["alive"]}
    dead_map = {item[0]: item for item in last_result["dead"]}

    items = []

    for original in whitelist:
        if original in alive_map:
            orig, proxy_type, ip, port, ms = alive_map[original]
            items.append((orig, proxy_type, ip, port, "alive", ms))
            continue

        if original in dead_map:
            orig, proxy_type, ip, port = dead_map[original]
            items.append((orig, proxy_type, ip, port, "dead", None))
            continue

        parsed = parse_proxy_line(original)
        if parsed:
            orig, ip, port, proxy_type = parsed
            items.append((orig, proxy_type, ip, port, "unknown", None))

    return items

def get_items_by_source_kind(kind: str):
    if kind == "socks":
        return [x for x in last_result["alive"] if x[1] == "socks5"]
    if kind == "mtproto":
        return [x for x in last_result["alive"] if x[1] == "mtproto"]
    if kind == "whitelist":
        return get_whitelist_items()
    return []


def get_item_from_callback_source(kind: str, page: int, pos: int):
    items = get_items_by_source_kind(kind)
    page_items, _ = paginate(items, page)
    if pos < 0 or pos >= len(page_items):
        return None
    return page_items[pos]

async def send_whitelist_page(query, page: int, lang: str):
    items = get_whitelist_items()
    title = tr(lang, "whitelist_title")

    page_items, total = paginate(items, page)

    if not items:
        await query.edit_message_text(
            f"{title}\n\n{tr(lang, 'whitelist_empty')}",
            reply_markup=InlineKeyboardMarkup(
                [[InlineKeyboardButton(tr(lang, "button_menu"), callback_data="action:menu")]]
            ),
        )
        return

    lines = [title, ""]
    keyboard = []

    for pos, item in enumerate(page_items):
        idx = page * PAGE_SIZE + pos + 1

        lines.append(format_whitelist_proxy(item, lang, idx))
        lines.append("")

        add_url = get_add_url(item[0], item[1])

        row = []
        if add_url:
            row.append(InlineKeyboardButton(tr(lang, "button_add", index=idx), url=add_url))

        row.append(
            InlineKeyboardButton(
                tr(lang, "button_to_my_pack", index=idx),
                callback_data=f"mypack:add:whitelist:{page}:{pos}"
            )
        )

        keyboard.append(row)

    nav = []
    if page > 0:
        nav.append(InlineKeyboardButton(tr(lang, "button_back"), callback_data=f"view:whitelist:{page - 1}"))
    if (page + 1) * PAGE_SIZE < total:
        nav.append(InlineKeyboardButton(tr(lang, "button_next"), callback_data=f"view:whitelist:{page + 1}"))
    if nav:
        keyboard.append(nav)

    keyboard.append([InlineKeyboardButton(tr(lang, "button_menu"), callback_data="action:menu")])

    await query.edit_message_text(
        "\n".join(lines).strip(),
        reply_markup=InlineKeyboardMarkup(keyboard),
    )

def update_streaks(state: dict, result: dict, increment_days: bool):
    streaks = load_streaks()

    alive_originals = {x[0] for x in result["alive"]}
    dead_originals = {x[0] for x in result["dead"]}
    base_originals = set(state.get("base_originals", []))

    relevant_alive = alive_originals & base_originals
    relevant_dead = dead_originals & base_originals

    for proxy in relevant_alive:
        if proxy not in streaks:
            streaks[proxy] = 1
        elif increment_days:
            streaks[proxy] += 1

    for proxy in relevant_dead:
        if proxy in streaks:
            del streaks[proxy]

    save_streaks(streaks)


def build_summary(result: dict, lang: str, state: dict | None = None) -> str:
    lines = [
        tr(lang, "summary_done"),
        "",
        tr(lang, "total", count=result["total"]),
        tr(lang, "alive_total", count=result["alive_count"]),
        tr(lang, "dead_total", count=result["dead_count"]),
        "",
        tr(lang, "socks_alive", count=result["socks_alive"]),
        tr(lang, "mtproto_alive", count=result["mtproto_alive"]),
    ]

    if state:
        if state.get("base_updated_at"):
            lines.append("")
            lines.append(tr(lang, "base_updated", value=state["base_updated_at"]))
        if state.get("last_check_at"):
            lines.append(tr(lang, "last_check", value=state["last_check_at"]))

        lines.append(tr(lang, "dead_since_base_count", count=len(state.get("dead_since_base", []))))

    if result["alive"]:
        lines.append("")
        lines.append(tr(lang, "top_fastest"))
        for i, (_, proxy_type, ip, port, ms) in enumerate(result["alive"][:5], start=1):
            lines.append(f"{i}. [{proxy_type}] {ip}:{port} — {ms} ms")

    return "\n".join(lines)


def get_dead_since_base(state: dict):
    dead_originals = state.get("dead_since_base", [])
    dead_map = {item[0]: item for item in last_result["dead"]}

    items = []
    seen = set()

    for original in dead_originals:
        if original in seen:
            continue
        seen.add(original)

        if original in dead_map:
            items.append(dead_map[original])
        else:
            parsed = parse_proxy_line(original)
            if parsed:
                original_line, ip, port, proxy_type = parsed
                items.append((original_line, proxy_type, ip, port))

    return items


def build_pack_text(items, title: str, lang: str):
    if not items:
        return tr(lang, "pack_empty", title=title)

    lines = [title, ""]
    for idx, item in enumerate(items, start=1):
        original, proxy_type, ip, port, ms = item
        add_url = get_add_url(original, proxy_type)
        lines.append(f"{idx}. [{proxy_type}] {ip}:{port} — {ms} ms")
        if add_url:
            lines.append(tr(lang, "pack_add", url=add_url))
        lines.append("")
    return "\n".join(lines).strip()


async def send_pack_message(target_message, items, title: str, lang: str):
    text = build_pack_text(items, title, lang)

    chunks = []
    current = ""

    for line in text.splitlines(True):
        if len(current) + len(line) > 3500:
            chunks.append(current)
            current = line
        else:
            current += line
    if current:
        chunks.append(current)

    for chunk in chunks:
        await target_message.reply_text(chunk)


async def send_alive_page(query, kind: str, page: int, lang: str):
    if kind == "socks":
        items = [x for x in last_result["alive"] if x[1] == "socks5"]
        title = tr(lang, "alive_socks_title")
    elif kind == "mtproto":
        items = [x for x in last_result["alive"] if x[1] == "mtproto"]
        title = tr(lang, "alive_mtproto_title")
    else:
        items = []
        title = tr(lang, "alive_generic_title")

    page_items, total = paginate(items, page)

    if not items:
        await query.edit_message_text(
            tr(lang, "pack_empty", title=title),
            reply_markup=InlineKeyboardMarkup(
                [[InlineKeyboardButton(tr(lang, "button_menu"), callback_data="action:menu")]]
            ),
        )
        return

    lines = [title, ""]
    keyboard = []

    for pos, item in enumerate(page_items):
        idx = page * PAGE_SIZE + pos + 1
    
        lines.append(format_alive_proxy(item, lang, idx))
        lines.append("")
    
        add_url = get_add_url(item[0], item[1])
    
        row = []
        if add_url:
            row.append(InlineKeyboardButton(tr(lang, "button_add", index=idx), url=add_url))
    
        row.append(
            InlineKeyboardButton(
                tr(lang, "button_to_my_pack", index=idx),
                callback_data=f"mypack:add:{kind}:{page}:{pos}"
            )
        )
    
        keyboard.append(row)

    nav = []
    if page > 0:
        nav.append(InlineKeyboardButton(tr(lang, "button_back"), callback_data=f"view:{kind}:{page - 1}"))
    if (page + 1) * PAGE_SIZE < total:
        nav.append(InlineKeyboardButton(tr(lang, "button_next"), callback_data=f"view:{kind}:{page + 1}"))
    if nav:
        keyboard.append(nav)

    keyboard.append([InlineKeyboardButton(tr(lang, "button_menu"), callback_data="action:menu")])

    await query.edit_message_text(
        "\n".join(lines).strip(),
        reply_markup=InlineKeyboardMarkup(keyboard),
    )


async def send_dead_since_base_page(query, page: int, lang: str):
    state = load_state()
    items = get_dead_since_base(state)
    title = tr(lang, "dead_since_base_title")

    page_items, total = paginate(items, page)

    if not items:
        await query.edit_message_text(
            tr(lang, "pack_empty", title=title),
            reply_markup=InlineKeyboardMarkup(
                [[InlineKeyboardButton(tr(lang, "button_menu"), callback_data="action:menu")]]
            ),
        )
        return

    lines = [title, ""]
    if state.get("base_updated_at"):
        lines.append(tr(lang, "base_updated", value=state["base_updated_at"]))
        lines.append("")

    for idx, item in enumerate(page_items, start=page * PAGE_SIZE + 1):
        lines.append(format_dead_proxy(item, idx))

    nav = []
    if page > 0:
        nav.append(InlineKeyboardButton(tr(lang, "button_back"), callback_data=f"view:dead_since_base:{page - 1}"))
    if (page + 1) * PAGE_SIZE < total:
        nav.append(InlineKeyboardButton(tr(lang, "button_next"), callback_data=f"view:dead_since_base:{page + 1}"))

    keyboard = []
    if nav:
        keyboard.append(nav)
    keyboard.append([InlineKeyboardButton(tr(lang, "button_menu"), callback_data="action:menu")])

    await query.edit_message_text(
        "\n".join(lines),
        reply_markup=InlineKeyboardMarkup(keyboard),
    )


async def send_users_page(query, page: int, lang: str):
    users = list(load_users().values())
    users.sort(key=lambda x: x.get("last_seen_ts", 0), reverse=True)

    page_items, total = paginate(users, page)

    if not users:
        await query.edit_message_text(
            tr(lang, "users_empty"),
            reply_markup=InlineKeyboardMarkup(
                [[InlineKeyboardButton(tr(lang, "button_menu"), callback_data="action:menu")]]
            ),
        )
        return

    lines = [f"{tr(lang, 'users_title')}\n", f"{tr(lang, 'total_users', count=len(users))}\n"]

    for idx, user in enumerate(page_items, start=page * PAGE_SIZE + 1):
        username = f"@{user['username']}" if user.get("username") else tr(lang, "no_username")
        full_name = user.get("full_name", "")
        lines.append(f"{idx}. {username} | {user.get('user_id')}")
        if full_name:
            lines.append(tr(lang, "name_label", value=full_name))
        lines.append(tr(lang, "last_login", value=user.get("last_seen", "-")))
        lines.append("")

    nav = []
    if page > 0:
        nav.append(InlineKeyboardButton(tr(lang, "button_back"), callback_data=f"owner:users:{page - 1}"))
    if (page + 1) * PAGE_SIZE < total:
        nav.append(InlineKeyboardButton(tr(lang, "button_next"), callback_data=f"owner:users:{page + 1}"))

    keyboard = []
    if nav:
        keyboard.append(nav)
    keyboard.append([InlineKeyboardButton(tr(lang, "button_menu"), callback_data="action:menu")])

    await query.edit_message_text(
        "\n".join(lines),
        reply_markup=InlineKeyboardMarkup(keyboard),
    )


async def notify_subscribers_localized(app: Application, key: str, target: str = "all", **kwargs):
    for item in load_subscribers():
        chat_type = item.get("chat_type", "private")

        if target == "private" and chat_type != "private":
            continue
        if target == "groups" and chat_type == "private":
            continue

        lang = get_user_language(item["chat_id"]) if chat_type == "private" else DEFAULT_LANGUAGE

        try:
            await app.bot.send_message(
                chat_id=item["chat_id"],
                text=tr(lang, key, **kwargs),
            )
        except Exception as e:
            print(f"[NOTIFY ERROR] {item['chat_id']}: {e}")


async def do_check_and_update_state(
    app: Application | None = None,
    announce_update: bool = False,
    increment_streak_days: bool = False,
):
    result = await run_proxy_check()
    state = load_state()

    state["last_alive_originals"] = [x[0] for x in result["alive"]]
    state["last_dead_originals"] = [x[0] for x in result["dead"]]
    state["last_check_at"] = now_str()

    base_originals = set(state.get("base_originals", []))
    dead_since_base = list(state.get("dead_since_base", []))
    dead_since_base_set = set(dead_since_base)

    for item in result["dead"]:
        original = item[0]
        if original in base_originals and original not in dead_since_base_set:
            dead_since_base.append(original)
            dead_since_base_set.add(original)

    state["dead_since_base"] = dead_since_base

    update_streaks(state, result, increment_days=increment_streak_days)
    save_state(state)

    if app and announce_update and state.get("base_updated_at"):
        await notify_subscribers_localized(app, "proxy_list_updated", target="all", value=state["base_updated_at"])

    return result


async def auto_check_loop(app: Application):
    await asyncio.sleep(10)
    while True:
        try:
            if not check_lock.locked():
                async with check_lock:
                    await do_check_and_update_state(
                        app,
                        increment_streak_days=True,
                    )
                    print("[AUTO CHECK] completed")
        except asyncio.CancelledError:
            raise
        except Exception as e:
            print(f"[AUTO CHECK ERROR] {e}")

        await asyncio.sleep(AUTO_CHECK_INTERVAL_SECONDS)


async def on_startup(app: Application):
    global auto_check_task
    auto_check_task = asyncio.create_task(auto_check_loop(app))


async def on_shutdown(app: Application):
    global auto_check_task
    if auto_check_task:
        auto_check_task.cancel()
        try:
            await auto_check_task
        except asyncio.CancelledError:
            pass


async def start_cmd(update: Update, context: ContextTypes.DEFAULT_TYPE):
    remember_user(update)
    remember_chat_subscription(update)

    # В группах не отвечаем вообще
    if not is_private_chat(update):
        return

    lang = get_update_language(update)
    await update.message.reply_text(
        tr(lang, "start_text"),
        reply_markup=build_main_menu(is_owner(update), lang, has_atlas_access(update)),
    )


async def handle_check_action(source, app: Application, for_owner: bool):
    lang = get_user_language(source.from_user.id) if getattr(source, "from_user", None) else DEFAULT_LANGUAGE
    show_atlas = bool(getattr(source, "from_user", None) and (source.from_user.id == OWNER_ID or source.from_user.id in ATLAS_USER_IDS))
    if check_lock.locked():
        await source.edit_message_text(tr(lang, "check_already_running"))
        return

    async with check_lock:
        await source.edit_message_text(tr(lang, "check_starting"))

        try:
            result = await do_check_and_update_state(
                app,
                increment_streak_days=False,
            )
        except Exception as e:
            await source.edit_message_text(tr(lang, "check_error", error=e))
            return

        state = load_state()
        text = build_summary(result, lang, state)
        markup = build_main_menu(for_owner, lang, show_atlas)
        await source.edit_message_text(text, reply_markup=markup)


async def handle_reloadbase_action(source, app: Application, for_owner: bool):
    lang = get_user_language(source.from_user.id) if getattr(source, "from_user", None) else DEFAULT_LANGUAGE
    show_atlas = bool(getattr(source, "from_user", None) and (source.from_user.id == OWNER_ID or source.from_user.id in ATLAS_USER_IDS))
    if not for_owner:
        await source.answer(tr(lang, "unavailable"), show_alert=True)
        return

    if check_lock.locked():
        await source.edit_message_text(tr(lang, "wait_current_check"))
        return

    async with check_lock:
        await source.edit_message_text(tr(lang, "reload_starting"))

        reset_proxy_tracking()

        try:
            result = await run_proxy_check()
        except Exception as e:
            await source.edit_message_text(tr(lang, "reload_error", error=e))
            return

        state = DEFAULT_STATE.copy()
        state["base_updated_at"] = now_str()
        state["base_originals"] = load_all_proxy_originals_from_input()
        state["dead_since_base"] = []
        state["last_alive_originals"] = [x[0] for x in result["alive"]]
        state["last_dead_originals"] = [x[0] for x in result["dead"]]
        state["last_check_at"] = now_str()
        save_state(state)

        fresh_streaks = {x[0]: 1 for x in result["alive"]}
        save_streaks(fresh_streaks)

        text = tr(lang, "base_updated_done", value=state["base_updated_at"])
        markup = build_main_menu(for_owner, lang, show_atlas)
        await source.edit_message_text(text, reply_markup=markup)

        await notify_subscribers_localized(app, "proxy_list_updated", target="all", value=state["base_updated_at"])


async def button_handler(update: Update, context: ContextTypes.DEFAULT_TYPE):
    query = update.callback_query

    remember_user(update)
    remember_chat_subscription(update)

    data = query.data or ""   # <-- СРАЗУ после query.answer()
    lang = get_update_language(update)

    # В группах кнопки не работают
    if not is_private_chat(update):
        await safe_answer_callback(query, tr(lang, "private_only"), show_alert=True)
        return

    for_owner = is_owner(update)
    atlas_access = has_atlas_access(update)

    if not data.startswith("lang:set:") and not data.startswith("mypack:add:") and not (
        data.startswith("owner:users:") and not for_owner
    ):
        await safe_answer_callback(query)

    if (
        not last_result["checked"]
        and data not in (
            "action:check",
            "action:reloadbase",
            "action:menu",
            "action:language",
            "action:quickconnect",
            "atlas:menu",
            "atlas:soft",
            "atlas:vless",
            "atlas:guides",
            "migration:key",
            "owner:atlas_notify",
        )
        and not data.startswith("lang:set:")
        and not data.startswith("atlas:soft:")
        and not data.startswith("atlas:vless:")
        and not data.startswith("atlas:guide:")
    ):
        await query.edit_message_text(tr(lang, "press_check_first"))
        return

    if data == "action:menu":
        state = load_state()
        text = build_summary(last_result, lang, state) if last_result["checked"] else tr(lang, "menu_text")
        await query.edit_message_text(text, reply_markup=build_main_menu(for_owner, lang, atlas_access))
        return

    if data == "action:language":
        await send_language_menu(query, update, for_owner)
        return

    if data == "action:quickconnect":
        await handle_quick_connect_action(query, update, for_owner)
        return

    if data.startswith("lang:set:"):
        new_lang = normalize_language(data.split(":")[2])
        set_user_language(update, new_lang)
        state = load_state()
        text = build_summary(last_result, new_lang, state) if last_result["checked"] else tr(new_lang, "menu_text")
        await query.edit_message_text(text, reply_markup=build_main_menu(for_owner, new_lang, atlas_access))
        await safe_answer_callback(query, tr(new_lang, "language_saved"))
        return

    if data == "migration:key":
        if not atlas_access:
            await safe_answer_callback(query, tr(lang, "unavailable"), show_alert=True)
            return
        await send_atlas_migration_key_panel(query, update)
        return

    if data.startswith("atlas:"):
        if not atlas_access:
            await safe_answer_callback(query, tr(lang, "unavailable"), show_alert=True)
            return
        await send_atlas_migration_panel(query, update)
        return

    if data == "atlas:menu":
        if not atlas_access:
            await safe_answer_callback(query, tr(lang, "unavailable"), show_alert=True)
            return
        await send_atlas_panel(query, update)
        return

    if data == "atlas:soft":
        if not atlas_access:
            await safe_answer_callback(query, tr(lang, "unavailable"), show_alert=True)
            return
        await send_atlas_soft_panel(query, update)
        return

    if data.startswith("atlas:soft:"):
        if not atlas_access:
            await safe_answer_callback(query, tr(lang, "unavailable"), show_alert=True)
            return
        kind = data.split(":")[2]
        if kind == "shadowsocks":
            await send_atlas_shadowsocks_browser_panel(query, update)
        if kind == "elyproxy":
            await send_atlas_elyproxy_panel(query, update)
        return

    if data == "atlas:vless":
        if not atlas_access:
            await safe_answer_callback(query, tr(lang, "unavailable"), show_alert=True)
            return
        await send_atlas_vless_panel(query, update)
        return

    if data.startswith("atlas:vless:"):
        if not atlas_access:
            await safe_answer_callback(query, tr(lang, "unavailable"), show_alert=True)
            return
        kind = data.split(":")[2]
        if kind in ("normal", "whitelist"):
            await send_atlas_vless_text(query, update, kind)
        return

    if data == "atlas:guides":
        if not atlas_access:
            await safe_answer_callback(query, tr(lang, "unavailable"), show_alert=True)
            return
        await send_atlas_guides_panel(query, update)
        return

    if data.startswith("atlas:guide:"):
        if not atlas_access:
            await safe_answer_callback(query, tr(lang, "unavailable"), show_alert=True)
            return
        kind = data.split(":")[2]
        if kind == "instruction":
            await send_atlas_instruction_panel(query, update)
            return
        if kind == "textdata":
            await send_atlas_text_data_panel(query, update)
            return
        if kind == "testurl":
            await send_atlas_test_url_panel(query, update)
            return
        if kind == "routingru":
            await send_atlas_routing_ru_panel(query, update)
            return
        return

    if data == "action:check":
        await handle_check_action(query, context.application, for_owner)
        return

    if data == "action:reloadbase":
        await handle_reloadbase_action(query, context.application, for_owner)
        return

    if data == "pack:all":
        await send_pack_message(query.message, last_result["alive"], tr(lang, "button_pack_all"), lang)
        return

    if data.startswith("view:socks:"):
        page = int(data.split(":")[2])
        await send_alive_page(query, "socks", page, lang)
        return

    if data.startswith("view:mtproto:"):
        page = int(data.split(":")[2])
        await send_alive_page(query, "mtproto", page, lang)
        return

    if data.startswith("view:dead_since_base:"):
        page = int(data.split(":")[2])
        await send_dead_since_base_page(query, page, lang)
        return

    if data.startswith("view:whitelist:"):
        page = int(data.split(":")[2])
        await send_whitelist_page(query, page, lang)
        return

    if data == "mypack:menu":
        await send_my_pack_menu(query, update)
        return

    if data == "mypack:best":
        await send_fastest_from_my_pack(query, update)
        return

    if data == "mypack:refresh":
        await refresh_my_pack_view(query, update)
        return

    if data == "mypack:remove_dead":
        await remove_dead_from_my_pack(query, update)
        return

    if data == "mypack:clear:confirm":
        await clear_my_pack_confirm(query, update)
        return

    if data == "mypack:clear:do":
        await clear_my_pack_do(query, update)
        return

    if data.startswith("mypack:view:all:"):
        page = int(data.split(":")[3])
        await send_my_pack_page(query, update, page, only_whitelist=False)
        return

    if data.startswith("mypack:view:white:"):
        page = int(data.split(":")[3])
        await send_my_pack_page(query, update, page, only_whitelist=True)
        return

    if data.startswith("mypack:add:"):
        parts = data.split(":")
        kind = parts[2]
        page = int(parts[3])
        pos = int(parts[4])

        item = get_item_from_callback_source(kind, page, pos)
        if not item:
            await safe_answer_callback(query, tr(lang, "proxy_not_found"), show_alert=True)
            return

        proxy_line = item[0]
        added, text = add_proxy_to_my_pack(update, proxy_line, kind)
        await safe_answer_callback(query, text, show_alert=True)
        return

    if data.startswith("owner:users:"):
        if not for_owner:
            await safe_answer_callback(query, tr(lang, "unavailable"), show_alert=True)
            return
        page = int(data.split(":")[2])
        await send_users_page(query, page, lang)
        return

    if data == "owner:atlas_notify":
        if not for_owner:
            await safe_answer_callback(query, tr(lang, "unavailable"), show_alert=True)
            return
        sent, failed = await send_atlas_notification(context.application)
        await safe_answer_callback(query, tr(lang, "atlas_notification_sent", sent=sent, failed=failed), show_alert=True)
        return


def main():
    ensure_files()
    token = load_token()

    app = (
        Application.builder()
        .token(token)
        .post_init(on_startup)
        .post_shutdown(on_shutdown)
        .build()
    )

    app.add_handler(CommandHandler("start", start_cmd))
    app.add_handler(CallbackQueryHandler(button_handler))

    print("Бот запущен.")
    app.run_polling(drop_pending_updates=True)


if __name__ == "__main__":
    main()
