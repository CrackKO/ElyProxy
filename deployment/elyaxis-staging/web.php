<?php

use Illuminate\Support\Facades\Route;
use Laravel\Socialite\Facades\Socialite;
use Illuminate\Support\Facades\Http;
use Illuminate\Support\Facades\Log;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Response;


Route::get('/safestorage', function () {
    return response('', 204);
});



Route::get('/safestorage/happwin', function () {
    $path = storage_path('app/public/downloads/setup-Happ.x64.exe');

    if (!file_exists($path)) {
        abort(404);
    }

    return response()->download($path, 'setup-Happ.x64.exe');
});

Route::get('/safestorage/happiosglobal', function () {
    return redirect('https://apps.apple.com/us/app/happ-proxy-utility/id6504287215');
});
Route::get('/safestorage/happiosru', function () {
    return redirect('https://apps.apple.com/ru/app/happ-proxy-utility-plus/id6746188973');
});

Route::get('/safestorage/elyproxywindows', function () {
    $path = storage_path('app/public/downloads/ElyProxy-v1.4.0-win-x64.zip');
    if (!file_exists($path)) {
        abort(404);
    }
    return response()->download($path, 'ElyProxy-v1.4.0-win-x64.zip');
});

Route::get('/safestorage/happandroidapk', function () {
    $path = storage_path('app/public/downloads/Happ.apk');

    if (!file_exists($path)) {
        abort(404);
    }

    return response()->download($path, 'Happ.apk');
});



$fetchAccessibleGuilds = function ($token) {
    // 1. Получаем все гильдии пользователя
    $userResponse = Http::withHeaders([
        'Authorization' => 'Bearer ' . $token,
    ])->timeout(15)->get('https://discord.com/api/users/@me/guilds');

    if ($userResponse->failed()) {
        throw new \Exception('Discord (user guilds) вернул статус ' . $userResponse->status());
    }

    $userGuilds = collect($userResponse->json());

    // 2. Получаем все гильдии, где состоит бот (по bot token)
    $botToken = config('services.discord_bot.token');

    if (!$botToken) {
        throw new \Exception('Не задан DISCORD_BOT_TOKEN в .env');
    }

    $botResponse = Http::withHeaders([
        'Authorization' => 'Bot ' . $botToken,
    ])->timeout(15)->get('https://discord.com/api/users/@me/guilds');

    if ($botResponse->failed()) {
        throw new \Exception('Discord (bot guilds) вернул статус ' . $botResponse->status());
    }

    $botGuildIds = collect($botResponse->json())
        ->pluck('id')
        ->all();

    // 3. Оставляем только гильдии, где:
    //    - у пользователя есть право ADMINISTRATOR
    //    - и в этой гильдии есть наш бот
    return $userGuilds
        ->filter(function ($guild) use ($botGuildIds) {
            $permissions = (int) ($guild['permissions'] ?? 0);
            $isAdmin = ($permissions & 0x00000008) === 0x00000008; // ADMINISTRATOR
            $hasBot  = in_array($guild['id'], $botGuildIds, true);

            return $isAdmin && $hasBot;
        })
        ->values();
};


// Главная страница

Route::get('/', function () {
    return view('index');
});

// Авторизация через Discord

Route::get('/login', function () {
    // если уже залогинен — сразу на дашборд
    if (session()->has('discord_user')) {
        return redirect('/dashboard');
    }

    return Socialite::driver('discord')
        ->scopes(['identify', 'guilds'])
        ->redirect();
})->name('login');

// Callback после Discord OAuth2

Route::get('/auth/discord/callback', function () {
    $user = Socialite::driver('discord')->user();

    session([
        'discord_user'  => $user,
        'discord_token' => $user->token,
    ]);

    return redirect('/dashboard');
});

Route::get('/api/guilds/{guildId}/voice-channels', function ($guildId) {
    if (!session()->has('discord_user')) {
        return response()->json(['message' => 'Unauthorized'], 401);
    }

    $baseUrl = rtrim(config('services.bot_api.base_url'), '/');
    $token   = config('services.bot_api.token');

    $resp = Http::withHeaders(['X-Internal-Token' => $token])
        ->timeout(20)
        ->get("{$baseUrl}/guilds/{$guildId}/voice-channels");

    return response($resp->body(), $resp->status())
        ->header('Content-Type', 'application/json');
});

Route::get('/api/guilds/{guildId}/panel', function ($guildId) {
    $baseUrl = rtrim(config('services.bot_api.base_url'), '/');
    $token   = config('services.bot_api.token');

    try {
        $response = Http::withHeaders([
            'X-Internal-Token' => $token,
        ])->timeout(20)->get("{$baseUrl}/guilds/{$guildId}/panel");

        if ($response->failed()) {
            Log::error('Bot API error', [
                'guildId' => $guildId,
                'status'  => $response->status(),
                'body'    => $response->body(),
            ]);

            return response()->json([
                'message' => 'Bot API error',
                'status'  => $response->status(),
            ], 500);
        }

        // Возвращаем JSON дальше во фронт
        return response()->json($response->json());

    } catch (\Throwable $e) {
        Log::error('HTTP error to Bot API', [
            'guildId' => $guildId,
            'error'   => $e->getMessage(),
        ]);

        return response()->json([
            'message' => 'HTTP error',
            'error'   => $e->getMessage(),
        ], 500);
    }
});



Route::get('/api/guilds/{guildId}/verification', function ($guildId) {
    // (опционально, но очень желательно) — не отдаём API гостям
    if (!session()->has('discord_user')) {
        return response()->json(['message' => 'Unauthorized'], 401);
    }

    $baseUrl = rtrim(config('services.bot_api.base_url'), '/');
    $token   = config('services.bot_api.token');

    try {
        $response = Http::withHeaders([
            'X-Internal-Token' => $token,
        ])->timeout(20)->get("{$baseUrl}/guilds/{$guildId}/verification");

        if ($response->failed()) {
            Log::error('Bot API error (verification)', [
                'guildId' => $guildId,
                'status'  => $response->status(),
                'body'    => $response->body(),
            ]);

            return response()->json([
                'message' => 'Bot API error',
                'status'  => $response->status(),
            ], 500);
        }

        return response()->json($response->json());

    } catch (\Throwable $e) {
        Log::error('HTTP error to Bot API (verification)', [
            'guildId' => $guildId,
            'error'   => $e->getMessage(),
        ]);

        return response()->json([
            'message' => 'HTTP error',
            'error'   => $e->getMessage(),
        ], 500);
    }
});

Route::get('/api/guilds/{guildId}/serverbage', function ($guildId) {
    if (!session()->has('discord_user')) {
        return response()->json(['message' => 'Unauthorized'], 401);
    }

    $baseUrl = rtrim(config('services.bot_api.base_url'), '/');
    $token   = config('services.bot_api.token');

    $resp = Http::withHeaders(['X-Internal-Token' => $token])
        ->timeout(20)
        ->get("{$baseUrl}/guilds/{$guildId}/serverbage");

    return response($resp->body(), $resp->status())
        ->header('Content-Type', 'application/json');
});

Route::get('/api/guilds/{guildId}/text-channels', function ($guildId) {
    if (!session()->has('discord_user')) {
        return response()->json(['message' => 'Unauthorized'], 401);
    }

    $baseUrl = rtrim(config('services.bot_api.base_url'), '/');
    $token   = config('services.bot_api.token');

    $resp = Http::withHeaders(['X-Internal-Token' => $token])
        ->timeout(20)
        ->get("{$baseUrl}/guilds/{$guildId}/text-channels");

    return response($resp->body(), $resp->status())
        ->header('Content-Type', 'application/json');
});


// Дашборд
Route::get('/dashboard', function () use ($fetchAccessibleGuilds) {
    $user  = session('discord_user');
    $token = session('discord_token');

    if (!$user || !$token) {
        return redirect('/login');
    }

    try {
        $guilds = $fetchAccessibleGuilds($token);

        return view('dashboard', [
            'user'   => $user,
            'guilds' => $guilds,
        ]);

    } catch (\Throwable $e) {
        return view('dashboard', [
            'user'   => $user,
            'guilds' => [],
            'error'  => 'Ошибка запроса: ' . $e->getMessage(),
        ]);
    }
});


Route::get('/api/guilds/{guildId}/xp-graph', function ($guildId) {
    if (!session()->has('discord_user')) {
        return response('', 401);
    }

    $baseUrl = rtrim(config('services.bot_api.base_url'), '/');
    $token   = config('services.bot_api.token');

    try {
        $resp = Http::withHeaders([
            'X-Internal-Token' => $token,
        ])->timeout(30)->get("{$baseUrl}/guilds/{$guildId}/xp-graph");

        if ($resp->failed()) {
            return response($resp->body(), $resp->status())
                ->header('Content-Type', $resp->header('Content-Type') ?? 'application/json')
                ->header('Cache-Control', 'no-store');
        }


        return response($resp->body(), 200)
            ->header('Content-Type', $resp->header('Content-Type') ?? 'image/png')
            ->header('Cache-Control', 'no-store');

    } catch (\Throwable $e) {
        Log::error('HTTP error to Bot API (xp-graph)', [
            'guildId' => $guildId,
            'error'   => $e->getMessage(),
        ]);
        return response('', 500);
    }
});

Route::get('/api/guilds/{guildId}/autovoice', function ($guildId) {
    if (!session()->has('discord_user')) {
        return response()->json(['message' => 'Unauthorized'], 401);
    }

    $baseUrl = rtrim(config('services.bot_api.base_url'), '/');
    $token   = config('services.bot_api.token');

    $resp = Http::withHeaders(['X-Internal-Token' => $token])
        ->timeout(20)
        ->get("{$baseUrl}/guilds/{$guildId}/autovoice");

    return response($resp->body(), $resp->status())
        ->header('Content-Type', 'application/json');
});

Route::post('/api/guilds/{guildId}/autovoice', function (Request $request, $guildId) use ($fetchAccessibleGuilds) {
    if (!session()->has('discord_user')) {
        return response()->json(['message' => 'Unauthorized'], 401);
    }

    // проверка: админ + бот на сервере
    $token = session('discord_token');
    try {
        $guilds = $fetchAccessibleGuilds($token);
        $allowed = $guilds->firstWhere('id', (string)$guildId);
        if (!$allowed) {
            return response()->json(['message' => 'Forbidden'], 403);
        }
    } catch (\Throwable $e) {
        return response()->json(['message' => 'Guild check failed', 'error' => $e->getMessage()], 500);
    }

    $baseUrl  = rtrim(config('services.bot_api.base_url'), '/');
    $internal = config('services.bot_api.token');
    $data = $request->json()->all() ?? [];

    $resp = Http::withHeaders([
            'X-Internal-Token' => $internal,
            'Accept' => 'application/json',
        ])
        ->timeout(25)
        ->post("{$baseUrl}/guilds/{$guildId}/autovoice", $data);

    return response($resp->body(), $resp->status())
        ->header('Content-Type', 'application/json');
});

Route::get('/api/guilds/{guildId}/autostats', function ($guildId) {
    if (!session()->has('discord_user')) {
        return response()->json(['message' => 'Unauthorized'], 401);
    }

    $baseUrl = rtrim(config('services.bot_api.base_url'), '/');
    $token   = config('services.bot_api.token');

    $resp = Http::withHeaders(['X-Internal-Token' => $token])
        ->timeout(20)
        ->get("{$baseUrl}/guilds/{$guildId}/autostats");

    return response($resp->body(), $resp->status())
        ->header('Content-Type', 'application/json');
});

Route::get('/api/guilds/{guildId}/minigames', function ($guildId) {
    if (!session()->has('discord_user')) {
        return response()->json(['message' => 'Unauthorized'], 401);
    }

    $baseUrl = rtrim(config('services.bot_api.base_url'), '/');
    $token   = config('services.bot_api.token');

    $resp = Http::withHeaders(['X-Internal-Token' => $token])
        ->timeout(20)
        ->get("{$baseUrl}/guilds/{$guildId}/minigames");

    return response($resp->body(), $resp->status())
        ->header('Content-Type', 'application/json');
});

$ensureLoggedIn = function () {
    if (!session()->has('discord_user')) {
        return response()->json(['message' => 'Unauthorized'], 401);
    }
    return null;
};

$ensureGuildAllowed = function ($guildId) use ($fetchAccessibleGuilds) {
    $token = session('discord_token');
    if (!$token) {
        return response()->json(['message' => 'Unauthorized'], 401);
    }

    try {
        $guilds = $fetchAccessibleGuilds($token);
        $allowed = $guilds->firstWhere('id', (string)$guildId);

        if (!$allowed) {
            return response()->json(['message' => 'Forbidden'], 403);
        }

        return null;
    } catch (\Throwable $e) {
        return response()->json([
            'message' => 'Guild check failed',
            'error'   => $e->getMessage(),
        ], 500);
    }
};

$botGet = function (string $path) {
    $baseUrl = rtrim(config('services.bot_api.base_url'), '/');
    $internal = config('services.bot_api.token');

    return Http::withHeaders([
            'X-Internal-Token' => $internal,
            'Accept'           => 'application/json',
        ])
        ->timeout(25)
        ->get("{$baseUrl}{$path}");
};

$botPost = function (string $path, array $data) {
    $baseUrl = rtrim(config('services.bot_api.base_url'), '/');
    $internal = config('services.bot_api.token');

    return Http::withHeaders([
            'X-Internal-Token' => $internal,
            'Accept'           => 'application/json',
        ])
        ->timeout(25)
        ->post("{$baseUrl}{$path}", $data);
};

$proxyJson = function ($resp) {
    return response($resp->body(), $resp->status())
        ->header('Content-Type', 'application/json');
};


/**
 * GET /api/guilds/{guildId}/roles
 * Список ролей сервера (для селектов и т.п.)
 */
Route::get('/api/guilds/{guildId}/roles', function ($guildId) use ($ensureLoggedIn, $botGet, $proxyJson) {
    if ($r = $ensureLoggedIn()) return $r;

    try {
        $resp = $botGet("/guilds/{$guildId}/roles");
        return $proxyJson($resp);
    } catch (\Throwable $e) {
        Log::error('HTTP error to Bot API (roles)', [
            'guildId' => $guildId,
            'error'   => $e->getMessage(),
        ]);
        return response()->json(['message' => 'HTTP error', 'error' => $e->getMessage()], 500);
    }
});


/**
 * GET /api/guilds/{guildId}/xp-roles
 * Получить текущие правила “роли за опыт”
 */
Route::get('/api/guilds/{guildId}/xp-roles', function ($guildId) use ($ensureLoggedIn, $botGet, $proxyJson) {
    if ($r = $ensureLoggedIn()) return $r;

    try {
        $resp = $botGet("/guilds/{$guildId}/xp-roles");
        return $proxyJson($resp);
    } catch (\Throwable $e) {
        Log::error('HTTP error to Bot API (xp-roles GET)', [
            'guildId' => $guildId,
            'error'   => $e->getMessage(),
        ]);
        return response()->json(['message' => 'HTTP error', 'error' => $e->getMessage()], 500);
    }
});


/**
 * POST /api/guilds/{guildId}/xp-roles
 * Твой фронт шлёт:
 *  - action=upsert: role_id, xp_voice(int), xp_text(int), badge_id(int 0..N)
 *  - action=delete: role_id
 */
Route::post('/api/guilds/{guildId}/xp-roles', function (Request $request, $guildId) use (
    $ensureLoggedIn,
    $ensureGuildAllowed,
    $botPost,
    $proxyJson
) {
    if ($r = $ensureLoggedIn()) return $r;
    if ($r = $ensureGuildAllowed($guildId)) return $r;

    $data = $request->json()->all() ?? [];

    $action = isset($data['action']) ? (string)$data['action'] : '';
    $roleId = isset($data['role_id']) ? trim((string)$data['role_id']) : '';

    if (!preg_match('/^\d+$/', $roleId)) {
        return response()->json(['message' => 'Invalid role_id'], 422);
    }

    if ($action === 'upsert') {
        // строго int >= 0
        $xpVoice = isset($data['xp_voice']) ? (int)$data['xp_voice'] : 0;
        $xpText  = isset($data['xp_text'])  ? (int)$data['xp_text']  : 0;
        $badgeId = isset($data['badge_id']) ? (int)$data['badge_id'] : 0;

        if ($xpVoice < 0 || $xpText < 0 || $badgeId < 0) {
            return response()->json(['message' => 'Invalid numbers'], 422);
        }

        $payload = [
            'action'   => 'upsert',
            'role_id'  => $roleId,
            'xp_voice' => $xpVoice,
            'xp_text'  => $xpText,
            'badge_id' => $badgeId,
        ];
    } elseif ($action === 'delete') {
        $payload = [
            'action'  => 'delete',
            'role_id' => $roleId,
        ];
    } else {
        return response()->json(['message' => 'Invalid action'], 422);
    }

    try {
        $resp = $botPost("/guilds/{$guildId}/xp-roles", $payload);

        if ($resp->failed()) {
            Log::error('Bot API error (xp-roles POST)', [
                'guildId' => $guildId,
                'status'  => $resp->status(),
                'body'    => $resp->body(),
                'payload' => $payload,
            ]);
        }

        return $proxyJson($resp);

    } catch (\Throwable $e) {
        Log::error('HTTP error to Bot API (xp-roles POST)', [
            'guildId' => $guildId,
            'error'   => $e->getMessage(),
        ]);
        return response()->json(['message' => 'HTTP error', 'error' => $e->getMessage()], 500);
    }
});

Route::post('/api/guilds/{guildId}/announce', function (Request $request, $guildId) use ($fetchAccessibleGuilds) {
    if (!session()->has('discord_user')) {
        return response()->json(['message' => 'Unauthorized'], 401);
    }

    // ЖЕЛАТЕЛЬНО: проверим что юзер админ и бот есть на сервере
    $token = session('discord_token');
    try {
        $guilds = $fetchAccessibleGuilds($token);
        $allowed = $guilds->firstWhere('id', (string)$guildId);
        if (!$allowed) {
            return response()->json(['message' => 'Forbidden'], 403);
        }
    } catch (\Throwable $e) {
        return response()->json(['message' => 'Guild check failed', 'error' => $e->getMessage()], 500);
    }

    $baseUrl = rtrim(config('services.bot_api.base_url'), '/');
    $internal = config('services.bot_api.token');

    $data = $request->json()->all() ?? [];

    $resp = Http::withHeaders([
            'X-Internal-Token' => $internal,
            'Accept' => 'application/json',
        ])
        ->timeout(25)
        ->post("{$baseUrl}/guilds/{$guildId}/announce", $data);

    return response($resp->body(), $resp->status())
        ->header('Content-Type', 'application/json');
});

Route::get('/api/guilds/{guildId}/roles', function ($guildId) {
    if (!session()->has('discord_user')) {
        return response()->json(['message' => 'Unauthorized'], 401);
    }

    $baseUrl = rtrim(config('services.bot_api.base_url'), '/');
    $token   = config('services.bot_api.token');

    $resp = Http::withHeaders(['X-Internal-Token' => $token])
        ->timeout(20)
        ->get("{$baseUrl}/guilds/{$guildId}/roles");

    return response($resp->body(), $resp->status())
        ->header('Content-Type', 'application/json');
});


Route::post('/api/guilds/{guildId}/verification', function (Request $request, $guildId) use ($fetchAccessibleGuilds) {
    if (!session()->has('discord_user')) {
        return response()->json(['message' => 'Unauthorized'], 401);
    }

    // проверка: админ + бот на сервере
    $token = session('discord_token');
    try {
        $guilds = $fetchAccessibleGuilds($token);
        $allowed = $guilds->firstWhere('id', (string)$guildId);
        if (!$allowed) {
            return response()->json(['message' => 'Forbidden'], 403);
        }
    } catch (\Throwable $e) {
        return response()->json(['message' => 'Guild check failed', 'error' => $e->getMessage()], 500);
    }

    $baseUrl = rtrim(config('services.bot_api.base_url'), '/');
    $internal = config('services.bot_api.token');

    $data = $request->json()->all() ?? [];

    $resp = Http::withHeaders([
            'X-Internal-Token' => $internal,
            'Accept' => 'application/json',
        ])
        ->timeout(25)
        ->post("{$baseUrl}/guilds/{$guildId}/verification", $data);

    return response($resp->body(), $resp->status())
        ->header('Content-Type', 'application/json');
});

Route::post('/api/guilds/{guildId}/autostats', function (Request $request, $guildId) use ($fetchAccessibleGuilds) {
    if (!session()->has('discord_user')) {
        return response()->json(['message' => 'Unauthorized'], 401);
    }

    // проверка: админ + бот на сервере
    $token = session('discord_token');
    try {
        $guilds = $fetchAccessibleGuilds($token);
        $allowed = $guilds->firstWhere('id', (string)$guildId);
        if (!$allowed) {
            return response()->json(['message' => 'Forbidden'], 403);
        }
    } catch (\Throwable $e) {
        return response()->json(['message' => 'Guild check failed', 'error' => $e->getMessage()], 500);
    }

    $baseUrl = rtrim(config('services.bot_api.base_url'), '/');
    $internal = config('services.bot_api.token');

    $data = $request->json()->all() ?? [];

    $resp = Http::withHeaders([
            'X-Internal-Token' => $internal,
            'Accept' => 'application/json',
        ])
        ->timeout(25)
        ->post("{$baseUrl}/guilds/{$guildId}/autostats", $data);

    return response($resp->body(), $resp->status())
        ->header('Content-Type', 'application/json');
});


Route::post('/api/guilds/{guildId}/minigames', function (Request $request, $guildId) use ($fetchAccessibleGuilds) {
    if (!session()->has('discord_user')) {
        return response()->json(['message' => 'Unauthorized'], 401);
    }

    // проверка: админ + бот на сервере
    $token = session('discord_token');
    try {
        $guilds = $fetchAccessibleGuilds($token);
        $allowed = $guilds->firstWhere('id', (string)$guildId);
        if (!$allowed) {
            return response()->json(['message' => 'Forbidden'], 403);
        }
    } catch (\Throwable $e) {
        return response()->json(['message' => 'Guild check failed', 'error' => $e->getMessage()], 500);
    }

    $baseUrl  = rtrim(config('services.bot_api.base_url'), '/');
    $internal = config('services.bot_api.token');

    $data = $request->json()->all() ?? [];

    $resp = Http::withHeaders([
            'X-Internal-Token' => $internal,
            'Accept' => 'application/json',
        ])
        ->timeout(25)
        ->post("{$baseUrl}/guilds/{$guildId}/minigames", $data);

    return response($resp->body(), $resp->status())
        ->header('Content-Type', 'application/json');
});

Route::post('/api/guilds/{guildId}/serverbage', function (Request $request, $guildId) use ($fetchAccessibleGuilds) {
    if (!session()->has('discord_user')) {
        return response()->json(['message' => 'Unauthorized'], 401);
    }

    // проверка: админ + бот на сервере
    $token = session('discord_token');
    try {
        $guilds = $fetchAccessibleGuilds($token);
        $allowed = $guilds->firstWhere('id', (string)$guildId);
        if (!$allowed) {
            return response()->json(['message' => 'Forbidden'], 403);
        }
    } catch (\Throwable $e) {
        return response()->json(['message' => 'Guild check failed', 'error' => $e->getMessage()], 500);
    }

    $baseUrl  = rtrim(config('services.bot_api.base_url'), '/');
    $internal = config('services.bot_api.token');

    $data = $request->json()->all() ?? [];

    $resp = Http::withHeaders([
            'X-Internal-Token' => $internal,
            'Accept' => 'application/json',
        ])
        ->timeout(25)
        ->post("{$baseUrl}/guilds/{$guildId}/serverbage", $data);

    return response($resp->body(), $resp->status())
        ->header('Content-Type', 'application/json');
});

// Выход

Route::get('/logout', function () {
    session()->forget(['discord_user', 'discord_token']);
    return redirect('/');
})->name('logout');
