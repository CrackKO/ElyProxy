import os
from collections import Counter, defaultdict
from datetime import datetime
from pathlib import Path

from telegram import InlineKeyboardButton, InlineKeyboardMarkup, Update
from telegram.ext import (
    Application,
    CallbackQueryHandler,
    CommandHandler,
    ContextTypes,
    MessageHandler,
    filters,
)

from elyaxis_storage import (
    activate_access_key,
    add_elyaxis_review,
    create_admin_access_key,
    ensure_elyaxis_data,
    get_elyaxis_user,
    get_system_status,
    list_broadcast_user_ids,
    list_elyaxis_reviews,
    list_elyaxis_users,
    now_month,
    remember_elyaxis_user,
    set_elyaxis_user_blocked,
    set_system_status,
)
from proxy import (
    OWNER_ID,
    send_atlas_elyproxy_panel,
    send_atlas_guides_panel,
    send_atlas_instruction_panel,
    send_atlas_routing_ru_panel,
    send_atlas_shadowsocks_browser_panel,
    send_atlas_soft_panel,
    send_atlas_test_url_panel,
    send_atlas_text_data_panel,
    send_atlas_vless_panel,
    send_atlas_vless_text,
)


TOKEN_FILE = "elyaxis_token.txt"
PAGE_SIZE = 8
ELYAXIS_ADMIN_IDS = {OWNER_ID}
UPDATE_NOTIFICATION_TEXT = "В боте было сделано обновление. Проверьте изменения"

VPN_STATUS_OPTIONS = {
    "all_ok": {
        "icon": "✅",
        "label": "Всё работает",
        "description": "Массовых проблем сейчас не отмечено.",
    },
    "whitelist_issue": {
        "icon": "⚠️",
        "label": "Есть проблемы с whitelist",
        "description": "Whitelist может работать нестабильно.",
    },
    "operator_issue": {
        "icon": "📶",
        "label": "Проблемы у конкретного оператора",
        "description": "Проблема ограничена отдельной сетью или оператором.",
    },
    "maintenance": {
        "icon": "🛠",
        "label": "Ведутся работы",
        "description": "Идут технические работы. Возможны перебои.",
    },
}

STATUS_OPERATOR_CODES = (
    "mts",
    "beeline",
    "megafon",
    "t2",
    "tmobile",
    "yota",
    "wifi",
    "ethernet",
)

REVIEW_PLATFORMS = {
    "ios": "IOS",
    "android": "Android",
    "windows": "Windows",
    "other": "Другое",
}

REVIEW_PROBLEMS = {
    "many_servers": "Не подключается к многим серверам",
    "all_servers": "Не работают все сервера",
    "whitelist": "Не работают White list",
}

REVIEW_NETWORKS = {
    "wifi": "WiFi",
    "ethernet": "Ethernet",
    "mts": "МТС",
    "beeline": "БИЛАЙН",
    "megafon": "МЕГАФОН",
    "t2": "Т2",
    "tmobile": "ТМОБАЙЛ",
    "yota": "ЙОТА",
    "wifi_operator": "WIFI + Оператор",
    "ethernet_operator": "Ethernet + Оператор",
}

MONTH_NAMES = {
    1: "январь",
    2: "февраль",
    3: "март",
    4: "апрель",
    5: "май",
    6: "июнь",
    7: "июль",
    8: "август",
    9: "сентябрь",
    10: "октябрь",
    11: "ноябрь",
    12: "декабрь",
}


def load_token() -> str:
    token = os.environ.get("ELYAXIS_BOT_TOKEN", "").strip()
    if token:
        return token

    token = Path(TOKEN_FILE).read_text(encoding="utf-8").strip() if Path(TOKEN_FILE).exists() else ""
    if not token:
        raise RuntimeError(
            "Файл elyaxis_token.txt пуст. Можно также задать переменную ELYAXIS_BOT_TOKEN."
        )
    return token


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


def telegram_chat_info(update: Update) -> dict:
    chat = update.effective_chat
    if not chat:
        return {}

    return {
        "chat_id": chat.id,
        "chat_type": chat.type,
        "title": getattr(chat, "title", "") or "",
        "username": getattr(chat, "username", "") or "",
    }


def remember(update: Update, action: str = ""):
    if update.effective_user:
        remember_elyaxis_user(
            telegram_user_info(update),
            telegram_chat_info(update),
            action=action,
        )


def is_admin_id(user_id: int | None) -> bool:
    return bool(user_id and user_id in ELYAXIS_ADMIN_IDS)


def is_admin(update: Update) -> bool:
    return bool(update.effective_user and is_admin_id(update.effective_user.id))


def has_access(update: Update) -> bool:
    user = update.effective_user
    if not user:
        return False
    if is_admin_id(user.id):
        return True

    record = get_elyaxis_user(user.id)
    return bool(record and record.get("access") and not record.get("blocked"))


def user_is_blocked(update: Update) -> bool:
    user = update.effective_user
    if not user or is_admin_id(user.id):
        return False

    record = get_elyaxis_user(user.id)
    return bool(record and record.get("blocked"))


async def safe_answer_callback(query, text: str | None = None, show_alert: bool = False):
    try:
        await query.answer(text=text, show_alert=show_alert)
    except Exception:
        pass


def paginate(items: list, page: int, page_size: int = PAGE_SIZE):
    page = max(0, page)
    start = page * page_size
    return items[start:start + page_size], len(items)


def status_option(code: str | None) -> dict:
    return VPN_STATUS_OPTIONS.get(code or "", VPN_STATUS_OPTIONS["all_ok"])


def status_short_line() -> str:
    status = get_system_status()
    option = status_option(status.get("code"))
    detail = status.get("detail") or ""
    suffix = f": {detail}" if detail else ""
    return f"{option['icon']} {option['label']}{suffix}"


def format_status_text(for_admin: bool = False) -> str:
    status = get_system_status()
    option = status_option(status.get("code"))
    detail = status.get("detail") or ""

    lines = [
        "Статус VPN",
        "",
        f"{option['icon']} {option['label']}",
    ]

    if detail:
        lines.append(detail)
    else:
        lines.append(option["description"])

    if status.get("updated_at"):
        lines.append("")
        lines.append(f"Обновлено: {status['updated_at']}")

    if for_admin:
        updated_by = status.get("updated_by_username") or status.get("updated_by_full_name") or status.get("updated_by_user_id")
        if updated_by:
            lines.append(f"Обновил: {updated_by}")

    return "\n".join(lines)


def build_status_menu(admin: bool = False):
    keyboard = []
    if admin:
        keyboard.append([InlineKeyboardButton("⚙️ Изменить статус", callback_data="admin:status")])
    keyboard.append([InlineKeyboardButton("🏠 Меню", callback_data="action:menu")])
    return InlineKeyboardMarkup(keyboard)


def build_admin_status_menu():
    return InlineKeyboardMarkup([
        [InlineKeyboardButton("✅ Всё работает", callback_data="admin:status:set:all_ok")],
        [InlineKeyboardButton("⚠️ Есть проблемы с whitelist", callback_data="admin:status:set:whitelist_issue")],
        [InlineKeyboardButton("📶 Проблемы у оператора", callback_data="admin:status:operator")],
        [InlineKeyboardButton("🛠 Ведутся работы", callback_data="admin:status:set:maintenance")],
        [InlineKeyboardButton("⚙️ Админ-панель", callback_data="admin:menu")],
    ])


def build_admin_status_operator_menu():
    keyboard = [
        [InlineKeyboardButton(REVIEW_NETWORKS[code], callback_data=f"admin:status:operator_set:{code}")]
        for code in STATUS_OPERATOR_CODES
    ]
    keyboard.append([InlineKeyboardButton("⬅️ Назад", callback_data="admin:status")])
    return InlineKeyboardMarkup(keyboard)


def build_main_menu(admin: bool = False):
    keyboard = [
        [
            InlineKeyboardButton("📦 Софт", callback_data="atlas:soft"),
            InlineKeyboardButton("🔑 Получить ключ", callback_data="atlas:vless"),
        ],
        [InlineKeyboardButton("📖 Гайды", callback_data="atlas:guides")],
        [
            InlineKeyboardButton("🧭 Статус VPN", callback_data="status:view"),
        ],
        [
            InlineKeyboardButton("📝 Отзывы", callback_data="review:home"),
            InlineKeyboardButton("📊 Все отзывы", callback_data="review:all"),
        ],
    ]

    if admin:
        keyboard.append([InlineKeyboardButton("⚙️ Админ-панель", callback_data="admin:menu")])

    return InlineKeyboardMarkup(keyboard)


def build_back_to_menu():
    return InlineKeyboardMarkup([
        [InlineKeyboardButton("🏠 Меню", callback_data="action:menu")],
    ])


def build_admin_menu():
    return InlineKeyboardMarkup([
        [InlineKeyboardButton("🧭 Статус VPN", callback_data="admin:status")],
        [InlineKeyboardButton("👥 Пользователи", callback_data="admin:users:0")],
        [InlineKeyboardButton("⛔ Закрыть доступ", callback_data="admin:block:0")],
        [InlineKeyboardButton("📢 Уведомить всех", callback_data="admin:notify_update")],
        [InlineKeyboardButton("✉️ Отправить сообщение", callback_data="admin:broadcast_custom")],
        [InlineKeyboardButton("🔑 Сгенерировать ключ", callback_data="admin:keygen")],
        [InlineKeyboardButton("🏠 Меню", callback_data="action:menu")],
    ])


def auth_prompt_text() -> str:
    return (
        "ElyAxis закрыт.\n\n"
        "Отправь одноразовый ключ сообщением, чтобы получить доступ. "
        "Один ключ может активировать только один человек."
    )


async def send_main_menu(target, update: Update, text: str | None = None):
    if text:
        message = text
    else:
        message = f"ElyAxis • закрытая VPN-панель\n\nСтатус: {status_short_line()}"
    markup = build_main_menu(is_admin(update))

    if hasattr(target, "edit_message_text"):
        await target.edit_message_text(message, reply_markup=markup)
    else:
        await target.reply_text(message, reply_markup=markup)


async def send_auth_prompt(update: Update, query=None):
    text = "Доступ к боту закрыт администратором." if user_is_blocked(update) else auth_prompt_text()
    if query:
        await query.edit_message_text(text)
    elif update.message:
        await update.message.reply_text(text)


async def activate_key_from_text(update: Update, raw_key: str):
    result = activate_access_key(
        raw_key,
        telegram_user_info(update),
        telegram_chat_info(update),
    )

    if result["ok"]:
        if result["status"] == "already_active":
            text = "Ключ уже активирован на тебя. Добро пожаловать обратно."
        else:
            text = "Ключ активирован. Добро пожаловать в ElyAxis."
        await send_main_menu(update.message, update, text=text)
        return

    if result["status"] == "blocked":
        await update.message.reply_text("Доступ к боту закрыт администратором.")
        return
    if result["status"] == "used":
        await update.message.reply_text("Этот ключ уже активировал другой пользователь.")
        return

    await update.message.reply_text("Ключ не найден. Проверь ввод или запроси ключ заново в старом боте.")


async def start_cmd(update: Update, context: ContextTypes.DEFAULT_TYPE):
    remember(update, action="/start")

    if context.args:
        await activate_key_from_text(update, context.args[0])
        return

    if has_access(update):
        await send_main_menu(update.message, update)
        return

    await send_auth_prompt(update)


async def broadcast_to_users(app: Application, text: str):
    sent = 0
    failed = 0
    for user_id in list_broadcast_user_ids():
        try:
            await app.bot.send_message(chat_id=user_id, text=text)
            sent += 1
        except Exception as exc:
            failed += 1
            print(f"[ELYAXIS BROADCAST ERROR] {user_id}: {exc}")

    return sent, failed


def access_status_text(user: dict) -> str:
    if user.get("blocked"):
        return "заблокирован"
    if user.get("access"):
        return "доступ есть"
    return "ключ не активирован"


def user_button_label(user: dict) -> str:
    name = user.get("username") or user.get("full_name") or str(user.get("user_id"))
    if user.get("username"):
        name = f"@{name}"
    prefix = "⛔" if user.get("blocked") else "✅" if user.get("access") else "🕓"
    return f"{prefix} {name} | {user.get('user_id')}"


def format_user_block(user: dict, index: int) -> str:
    username = f"@{user['username']}" if user.get("username") else "без username"
    lines = [
        f"{index}. {username} | {user.get('user_id')}",
        f"Имя: {user.get('full_name') or '-'}",
        f"Статус: {access_status_text(user)}",
        f"Первый вход: {user.get('first_seen') or '-'}",
        f"Последний вход: {user.get('last_seen') or '-'}",
        f"Язык Telegram: {user.get('language_code') or '-'}",
        f"Визитов/действий: {user.get('visit_count') or 0}",
        f"Источник доступа: {user.get('access_source') or '-'}",
        f"Последнее действие: {user.get('last_action') or '-'}",
    ]
    if user.get("last_review_at"):
        lines.append(f"Последний отзыв: {user.get('last_review_at')}")
    return "\n".join(lines)


async def send_admin_menu(query):
    await query.edit_message_text("Админ-панель ElyAxis.", reply_markup=build_admin_menu())


async def send_status_panel(query, update: Update):
    await query.edit_message_text(
        format_status_text(for_admin=is_admin(update)),
        reply_markup=build_status_menu(is_admin(update)),
    )


async def send_admin_status_panel(query):
    await query.edit_message_text(
        f"{format_status_text(for_admin=True)}\n\nВыбери новый статус.",
        reply_markup=build_admin_status_menu(),
    )


async def send_admin_status_operator_panel(query):
    await query.edit_message_text(
        "Выбери оператора или сеть, где сейчас есть проблема.",
        reply_markup=build_admin_status_operator_menu(),
    )


async def send_admin_users_page(query, page: int):
    users = list_elyaxis_users()
    page_items, total = paginate(users, page)

    if not users:
        await query.edit_message_text("Пользователей пока нет.", reply_markup=build_admin_menu())
        return

    lines = [f"Пользователи ElyAxis: {len(users)}", ""]
    for idx, user in enumerate(page_items, start=page * PAGE_SIZE + 1):
        lines.append(format_user_block(user, idx))
        lines.append("")

    keyboard = []
    nav = []
    if page > 0:
        nav.append(InlineKeyboardButton("⬅️ Назад", callback_data=f"admin:users:{page - 1}"))
    if (page + 1) * PAGE_SIZE < total:
        nav.append(InlineKeyboardButton("➡️ Дальше", callback_data=f"admin:users:{page + 1}"))
    if nav:
        keyboard.append(nav)
    keyboard.append([InlineKeyboardButton("⚙️ Админ-панель", callback_data="admin:menu")])

    await query.edit_message_text("\n".join(lines).strip(), reply_markup=InlineKeyboardMarkup(keyboard))


async def send_admin_block_page(query, page: int):
    users = list_elyaxis_users()
    page_items, total = paginate(users, page)

    if not users:
        await query.edit_message_text("Пользователей пока нет.", reply_markup=build_admin_menu())
        return

    keyboard = [
        [InlineKeyboardButton(user_button_label(user), callback_data=f"admin:user:{user['user_id']}:{page}")]
        for user in page_items
    ]

    nav = []
    if page > 0:
        nav.append(InlineKeyboardButton("⬅️ Назад", callback_data=f"admin:block:{page - 1}"))
    if (page + 1) * PAGE_SIZE < total:
        nav.append(InlineKeyboardButton("➡️ Дальше", callback_data=f"admin:block:{page + 1}"))
    if nav:
        keyboard.append(nav)

    keyboard.append([InlineKeyboardButton("⚙️ Админ-панель", callback_data="admin:menu")])
    await query.edit_message_text(
        "Выбери пользователя, которому нужно закрыть или открыть доступ.",
        reply_markup=InlineKeyboardMarkup(keyboard),
    )


async def send_admin_user_detail(query, user_id: int, page: int):
    user = get_elyaxis_user(user_id)
    if not user:
        await query.edit_message_text("Пользователь не найден.", reply_markup=build_admin_menu())
        return

    should_block = not bool(user.get("blocked"))
    action_text = "⛔ Закрыть доступ" if should_block else "✅ Открыть доступ"
    action_value = "1" if should_block else "0"

    keyboard = InlineKeyboardMarkup([
        [InlineKeyboardButton(action_text, callback_data=f"admin:block_do:{user_id}:{action_value}:{page}")],
        [InlineKeyboardButton("⬅️ К списку", callback_data=f"admin:block:{page}")],
        [InlineKeyboardButton("⚙️ Админ-панель", callback_data="admin:menu")],
    ])
    await query.edit_message_text(format_user_block(user, 1), reply_markup=keyboard)


async def handle_admin_keygen(query, update: Update):
    result = create_admin_access_key(telegram_user_info(update))
    key = result["key"]
    text = (
        "Новый одноразовый ключ создан:\n\n"
        f"<code>{key}</code>\n\n"
        "Его может активировать только один пользователь."
    )
    keyboard = InlineKeyboardMarkup([
        [InlineKeyboardButton("⚙️ Админ-панель", callback_data="admin:menu")],
    ])
    await query.edit_message_text(text, reply_markup=keyboard, parse_mode="HTML")


def build_review_platform_menu():
    keyboard = [
        [
            InlineKeyboardButton(REVIEW_PLATFORMS["ios"], callback_data="review:platform:ios"),
            InlineKeyboardButton(REVIEW_PLATFORMS["android"], callback_data="review:platform:android"),
        ],
        [
            InlineKeyboardButton(REVIEW_PLATFORMS["windows"], callback_data="review:platform:windows"),
            InlineKeyboardButton(REVIEW_PLATFORMS["other"], callback_data="review:platform:other"),
        ],
    ]
    keyboard.append([InlineKeyboardButton("❌ Отменить", callback_data="review:cancel")])
    return InlineKeyboardMarkup(keyboard)


def build_review_problem_menu():
    keyboard = [
        [InlineKeyboardButton(label, callback_data=f"review:problem:{code}")]
        for code, label in REVIEW_PROBLEMS.items()
    ]
    keyboard.append([InlineKeyboardButton("❌ Отменить", callback_data="review:cancel")])
    return InlineKeyboardMarkup(keyboard)


def build_review_network_menu():
    keyboard = [
        [
            InlineKeyboardButton(REVIEW_NETWORKS["wifi"], callback_data="review:network:wifi"),
            InlineKeyboardButton(REVIEW_NETWORKS["ethernet"], callback_data="review:network:ethernet"),
        ],
        [
            InlineKeyboardButton(REVIEW_NETWORKS["mts"], callback_data="review:network:mts"),
            InlineKeyboardButton(REVIEW_NETWORKS["beeline"], callback_data="review:network:beeline"),
        ],
        [
            InlineKeyboardButton(REVIEW_NETWORKS["megafon"], callback_data="review:network:megafon"),
            InlineKeyboardButton(REVIEW_NETWORKS["t2"], callback_data="review:network:t2"),
        ],
        [
            InlineKeyboardButton(REVIEW_NETWORKS["tmobile"], callback_data="review:network:tmobile"),
            InlineKeyboardButton(REVIEW_NETWORKS["yota"], callback_data="review:network:yota"),
        ],
        [
            InlineKeyboardButton(REVIEW_NETWORKS["wifi_operator"], callback_data="review:network:wifi_operator"),
            InlineKeyboardButton(REVIEW_NETWORKS["ethernet_operator"], callback_data="review:network:ethernet_operator"),
        ],
    ]
    keyboard.append([InlineKeyboardButton("❌ Отменить", callback_data="review:cancel")])
    return InlineKeyboardMarkup(keyboard)


def build_review_home_menu():
    return InlineKeyboardMarkup([
        [InlineKeyboardButton("📝 Оставить отзыв", callback_data="review:start")],
        [InlineKeyboardButton("📊 Все отзывы", callback_data="review:all")],
        [InlineKeyboardButton("🏠 Меню", callback_data="action:menu")],
    ])


def build_review_city_menu():
    return InlineKeyboardMarkup([
        [InlineKeyboardButton("Пропустить город", callback_data="review:city:skip")],
        [InlineKeyboardButton("❌ Отменить", callback_data="review:cancel")],
    ])


def selected_review_value(draft: dict, key: str, labels: dict) -> str:
    return labels.get(draft.get(key), "не выбрано")


def format_review_draft(draft: dict, city: str | None = None) -> str:
    city_text = city if city is not None else draft.get("city", "")
    return "\n".join([
        f"Платформа: {selected_review_value(draft, 'platform', REVIEW_PLATFORMS)}",
        f"Проблема: {selected_review_value(draft, 'problem', REVIEW_PROBLEMS)}",
        f"Сеть: {selected_review_value(draft, 'network', REVIEW_NETWORKS)}",
        f"Город: {city_text or 'не указан'}",
    ])


async def finish_review(update: Update, context: ContextTypes.DEFAULT_TYPE, query=None, city: str = ""):
    draft = context.user_data.get("review", {})
    platform = REVIEW_PLATFORMS.get(draft.get("platform"))
    problem = REVIEW_PROBLEMS.get(draft.get("problem"))
    network = REVIEW_NETWORKS.get(draft.get("network"))

    if not platform or not problem or not network:
        context.user_data.pop("review", None)
        text = "Не получилось собрать отзыв. Попробуй ещё раз."
        if query:
            await query.edit_message_text(text, reply_markup=build_back_to_menu())
        elif update.message:
            await update.message.reply_text(text, reply_markup=build_back_to_menu())
        return

    add_elyaxis_review(
        telegram_user_info(update),
        telegram_chat_info(update),
        platform=platform,
        problem=problem,
        network=network,
        city=city,
    )
    context.user_data.pop("review", None)

    text = f"Статистика записана.\n\n{format_review_draft(draft, city=city.strip())}"
    if query:
        await query.edit_message_text(text, reply_markup=build_back_to_menu())
    elif update.message:
        await update.message.reply_text(text, reply_markup=build_back_to_menu())


def format_count_block(title: str, counter: Counter, limit: int = 8) -> list[str]:
    if not counter:
        return []

    max_count = max(counter.values())
    total = sum(counter.values())
    lines = [title]
    for label, count in counter.most_common(limit):
        width = max(1, round((count / max_count) * 10))
        percent = round((count / total) * 100)
        lines.append(f"{label}: {count} ({percent}%) | {'#' * width}")
    return lines


def month_title(month: str) -> str:
    try:
        dt = datetime.strptime(month, "%Y-%m")
        return f"{MONTH_NAMES.get(dt.month, month)} {dt.year}"
    except Exception:
        return month


def format_review_stats() -> str:
    month = now_month()
    reviews = list_elyaxis_reviews(month=month)
    if not reviews:
        return (
            f"Все отзывы • {month_title(month)}\n\n"
            "За этот месяц отзывов пока нет.\n\n"
            "Когда пользователи оставят отзывы, здесь появится статистика по платформам, сетям, городам и дням."
        )

    problem_counts = Counter(item.get("problem") or "Не указано" for item in reviews)
    network_counts = Counter(item.get("network") or "Не указано" for item in reviews)
    platform_counts = Counter(item.get("platform") or "Не указано" for item in reviews)
    city_counts = Counter(item.get("city") or "город не указан" for item in reviews)
    today = datetime.now().strftime("%Y-%m-%d")
    today_count = sum(1 for item in reviews if item.get("date") == today)

    day_network_counts = defaultdict(Counter)
    network_problem_counts = defaultdict(Counter)
    combo_counts = Counter()
    for item in reviews:
        date_label = item.get("date") or ""
        try:
            date_label = datetime.strptime(date_label, "%Y-%m-%d").strftime("%d.%m")
        except Exception:
            pass
        network = item.get("network") or "Не указано"
        problem = item.get("problem") or "Не указано"
        platform = item.get("platform") or "Не указано"
        city = item.get("city") or "город не указан"
        day_network_counts[date_label][network] += 1
        network_problem_counts[network][problem] += 1
        combo_counts[(network, problem, platform, city)] += 1

    lines = [
        f"Все отзывы • {month_title(month)}",
        "",
        f"Всего за месяц: {len(reviews)}",
        f"Сегодня: {today_count}",
        "Авторы отзывов в общей статистике не показываются.",
        "",
    ]

    for block in (
        format_count_block("Проблемы:", problem_counts),
        format_count_block("Сети/операторы:", network_counts),
        format_count_block("Платформы:", platform_counts),
        format_count_block("Города:", city_counts),
    ):
        if block:
            lines.extend(block)
            lines.append("")

    lines.append("Главная проблема по сетям:")
    for network, counter in sorted(network_problem_counts.items()):
        problem, count = counter.most_common(1)[0]
        lines.append(f"{network}: {problem} ({count})")
    lines.append("")

    lines.append("Частые связки:")
    for (network, problem, platform, city), count in combo_counts.most_common(8):
        lines.append(f"{count} | {network} | {platform} | {city} | {problem}")
    lines.append("")

    lines.append("По дням:")
    for day in sorted(day_network_counts):
        top = ", ".join(f"{network} {count}" for network, count in day_network_counts[day].most_common(4))
        lines.append(f"{day}: {sum(day_network_counts[day].values())} | {top}")
    lines.append("")

    lines.append("Последние записи:")
    for item in reviews[:10]:
        city = item.get("city") or "город не указан"
        created_at = item.get("created_at") or "-"
        lines.append(
            f"{created_at} | {item.get('platform')} | "
            f"{item.get('network')} | {city} | {item.get('problem')}"
        )

    return "\n".join(lines).strip()


async def handle_atlas_callback(update: Update, data: str):
    query = update.callback_query

    if data == "atlas:menu":
        await send_main_menu(query, update)
        return True
    if data == "atlas:soft":
        await send_atlas_soft_panel(query, update)
        return True
    if data.startswith("atlas:soft:"):
        kind = data.split(":")[2]
        if kind == "shadowsocks":
            await send_atlas_shadowsocks_browser_panel(query, update)
        elif kind == "elyproxy":
            await send_atlas_elyproxy_panel(query, update)
        return True
    if data == "atlas:vless":
        await send_atlas_vless_panel(query, update)
        return True
    if data.startswith("atlas:vless:"):
        kind = data.split(":")[2]
        if kind in ("normal", "whitelist"):
            await send_atlas_vless_text(query, update, kind)
        return True
    if data == "atlas:guides":
        await send_atlas_guides_panel(query, update)
        return True
    if data.startswith("atlas:guide:"):
        kind = data.split(":")[2]
        if kind == "instruction":
            await send_atlas_instruction_panel(query, update)
        elif kind == "textdata":
            await send_atlas_text_data_panel(query, update)
        elif kind == "testurl":
            await send_atlas_test_url_panel(query, update)
        elif kind == "routingru":
            await send_atlas_routing_ru_panel(query, update)
        return True

    return False


async def button_handler(update: Update, context: ContextTypes.DEFAULT_TYPE):
    query = update.callback_query
    data = query.data or ""
    remember(update, action=data)
    await safe_answer_callback(query)

    if data == "noop":
        return

    if data == "action:menu":
        if has_access(update):
            await send_main_menu(query, update)
        else:
            await send_auth_prompt(update, query=query)
        return

    if not has_access(update):
        await send_auth_prompt(update, query=query)
        return

    if data.startswith("atlas:"):
        await handle_atlas_callback(update, data)
        return

    if data == "status:view":
        await send_status_panel(query, update)
        return

    if data == "review:home":
        text = (
            "Отзывы\n\n"
            "Здесь можно быстро отметить, где и при каких условиях появилась проблема. "
            "Личные данные в общей статистике не показываются."
        )
        await query.edit_message_text(text, reply_markup=build_review_home_menu())
        return

    if data == "review:start":
        context.user_data["review"] = {}
        await query.edit_message_text(
            "Отзыв • шаг 1/4\n\nВыбери платформу, на которой возникла проблема.",
            reply_markup=build_review_platform_menu(),
        )
        return

    if data.startswith("review:platform:"):
        code = data.split(":")[2]
        if code in REVIEW_PLATFORMS:
            context.user_data.setdefault("review", {})["platform"] = code
            await query.edit_message_text(
                "Отзыв • шаг 2/4\n\nВыбери тип проблемы.",
                reply_markup=build_review_problem_menu(),
            )
        return

    if data.startswith("review:problem:"):
        code = data.split(":")[2]
        if code in REVIEW_PROBLEMS:
            context.user_data.setdefault("review", {})["problem"] = code
            await query.edit_message_text(
                "Отзыв • шаг 3/4\n\nВыбери оператора или сеть.",
                reply_markup=build_review_network_menu(),
            )
        return

    if data.startswith("review:network:"):
        code = data.split(":")[2]
        if code in REVIEW_NETWORKS:
            draft = context.user_data.setdefault("review", {})
            draft["network"] = code
            draft["awaiting_city"] = True
            draft["city"] = ""
            await query.edit_message_text(
                f"Отзыв • шаг 4/4\n\n{format_review_draft(draft)}\n\n"
                "Отправь город сообщением или пропусти этот шаг.",
                reply_markup=build_review_city_menu(),
            )
        return

    if data == "review:city:skip":
        await finish_review(update, context, query=query, city="")
        return

    if data == "review:cancel":
        context.user_data.pop("review", None)
        await query.edit_message_text("Отменено.", reply_markup=build_back_to_menu())
        return

    if data == "review:all":
        await query.edit_message_text(format_review_stats(), reply_markup=build_back_to_menu())
        return

    if data.startswith("admin:"):
        if not is_admin(update):
            await safe_answer_callback(query, "Недоступно.", show_alert=True)
            return

        if data == "admin:menu":
            context.user_data.pop("admin_state", None)
            await send_admin_menu(query)
            return

        if data == "admin:status":
            await send_admin_status_panel(query)
            return

        if data.startswith("admin:status:set:"):
            code = data.split(":")[3]
            option = status_option(code)
            set_system_status(
                code,
                option["label"],
                telegram_user_info(update),
                detail="",
            )
            await send_status_panel(query, update)
            return

        if data == "admin:status:operator":
            await send_admin_status_operator_panel(query)
            return

        if data.startswith("admin:status:operator_set:"):
            network_code = data.split(":")[3]
            network_label = REVIEW_NETWORKS.get(network_code, network_code)
            option = status_option("operator_issue")
            set_system_status(
                "operator_issue",
                option["label"],
                telegram_user_info(update),
                detail=f"Оператор/сеть: {network_label}",
            )
            await send_status_panel(query, update)
            return

        if data.startswith("admin:users:"):
            await send_admin_users_page(query, int(data.split(":")[2]))
            return

        if data.startswith("admin:block:"):
            await send_admin_block_page(query, int(data.split(":")[2]))
            return

        if data.startswith("admin:user:"):
            parts = data.split(":")
            await send_admin_user_detail(query, int(parts[2]), int(parts[3]))
            return

        if data.startswith("admin:block_do:"):
            parts = data.split(":")
            user_id = int(parts[2])
            blocked = parts[3] == "1"
            page = int(parts[4])
            set_elyaxis_user_blocked(user_id, blocked, telegram_user_info(update))
            await send_admin_user_detail(query, user_id, page)
            return

        if data == "admin:notify_update":
            sent, failed = await broadcast_to_users(context.application, UPDATE_NOTIFICATION_TEXT)
            await safe_answer_callback(
                query,
                f"Отправлено: {sent}. Ошибок: {failed}.",
                show_alert=True,
            )
            return

        if data == "admin:broadcast_custom":
            context.user_data["admin_state"] = "awaiting_broadcast_text"
            keyboard = InlineKeyboardMarkup([
                [InlineKeyboardButton("❌ Отменить", callback_data="admin:menu")],
            ])
            await query.edit_message_text("Напиши сообщение, которое нужно разослать всем пользователям.", reply_markup=keyboard)
            return

        if data == "admin:keygen":
            await handle_admin_keygen(query, update)
            return


async def message_handler(update: Update, context: ContextTypes.DEFAULT_TYPE):
    remember(update, action="message")

    if is_admin(update) and context.user_data.get("admin_state") == "awaiting_broadcast_text":
        text = update.message.text.strip()
        context.user_data.pop("admin_state", None)
        if not text:
            await update.message.reply_text("Пустое сообщение не отправлено.", reply_markup=build_admin_menu())
            return

        sent, failed = await broadcast_to_users(context.application, text)
        await update.message.reply_text(
            f"Сообщение отправлено.\nОтправлено: {sent}\nОшибок: {failed}",
            reply_markup=build_admin_menu(),
        )
        return

    review_draft = context.user_data.get("review")
    if has_access(update) and review_draft and review_draft.get("awaiting_city"):
        city = update.message.text.strip()[:80]
        await finish_review(update, context, city=city)
        return

    if not has_access(update):
        await activate_key_from_text(update, update.message.text)
        return

    await send_main_menu(update.message, update, text="Выбери действие в меню.")


def main():
    ensure_elyaxis_data()
    app = Application.builder().token(load_token()).build()

    app.add_handler(CommandHandler("start", start_cmd))
    app.add_handler(CallbackQueryHandler(button_handler))
    app.add_handler(MessageHandler(filters.TEXT & ~filters.COMMAND, message_handler))

    print("ElyAxis bot started.")
    app.run_polling(drop_pending_updates=True)


if __name__ == "__main__":
    main()
