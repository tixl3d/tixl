<?php
/**
 * TiXL collab backend - the party-matching lobby, whispers and WebRTC signaling for the
 * editor's session browser. Self-hostable: drop this file (plus .htaccess) into any PHP 8
 * web space; storage is SQLite when available, JSON files otherwise.
 *
 * Editors talk to it with plain HTTPS + JSON:
 *   POST ?op=register      host puts a session in the list (room, key, title, ...)
 *   POST ?op=heartbeat     host keeps it alive (key required)
 *   POST ?op=update        host edits the entry / unlists it (key required)
 *   POST ?op=unregister    host deletes the entry (key required)
 *   GET  ?op=list          the session browser: every listed, non-expired session
 *   POST ?op=whisper       a visitor messages the host of a session
 *   GET  ?op=whispers      host: every whisper for its room; visitor: for one name
 *   POST ?op=signal        relay-free WebRTC signaling: put an offer/answer
 *   GET  ?op=signal        relay-free WebRTC signaling: take one
 *   POST ?op=verify        Discord token check: {token} -> {name, lang, id, verified}
 *
 * Remote management (an owner manages unattended installations):
 *   POST ?op=remote-pair-start    owner: create a 6-digit pairing code
 *   POST ?op=remote-pair-status   owner: wait until the code is redeemed
 *   POST ?op=remote-pair-redeem   installation: redeem the code, receive a device secret
 *   POST ?op=remote-heartbeat     installation: presence + take connect/disconnect commands
 *   POST ?op=remote-report        installation: report the hosted room and session state
 *   POST ?op=remote-attach        owner: confirm the controller has joined
 *   POST ?op=remote-list          owner: paired computers with presence and state
 *   POST ?op=remote-connect       owner: ask an online installation to host a managed session
 *   POST ?op=remote-disconnect    owner: end the managed session
 *   POST ?op=remote-set           owner: rename or enable/disable a computer
 *   POST ?op=remote-forget        owner: remove a paired computer
 *   POST ?op=remote-unpair        installation: remove its own pairing
 *
 * The client is the TiXL editor (a desktop app), not a browser: no cookies, no CSRF,
 * JSON in and out. Secrets (room keys) are stored hashed. Everything user-visible is
 * length-capped and control-character free.
 */

declare(strict_types=1);

header('Content-Type: application/json; charset=utf-8');
header('X-Content-Type-Options: nosniff');

const ROOM_TTL_SECONDS = 90;          // a session dies from the list without heartbeats
const SIGNAL_TTL_SECONDS = 300;       // an unanswered offer goes away
const MAX_ROOMS = 200;
const MAX_WHISPERS_PER_ROOM = 40;
const MAX_NAME = 48;
const MAX_TEXT = 256;
const MAX_TICKETS_PER_ROOM = 16;
const MAX_MSGS_PER_TICKET = 32;
const MAX_TITLE = 64;
const MAX_PURPOSE = 24;
const MAX_PROJECT = 64;
const MAX_LANG = 12;
const MAX_ROOM_CODE = 12;
const MAX_KEY = 64;


// Remote management: a logged-in owner pairs unattended TiXL machines once, then reconnects
// to them whenever they are online. The pairing code is short because a human types it on the
// installation; the device secret (issued on redemption) is what authenticates every request.
const PAIR_TTL_SECONDS = 600;         // a code shown on the controller expires after 10 minutes
const PAIR_MAX_ATTEMPTS = 12;         // wrong guesses allowed against one code
const PAIR_ATTEMPT_WINDOW = 300;      // global brute-force window
const PAIR_ATTEMPT_LIMIT = 200;       // attempts from anywhere within the window
const REMOTE_ONLINE_SECONDS = 35;     // a device without a heartbeat for this long is offline
const REMOTE_CONNECT_TTL = 300;       // an unanswered connect request is given up on
const MAX_DEVICES = 50;
const MAX_DEVICE_NAME = 48;
const MAX_DEVICE_PROJECT = 64;
const MAX_DEVICE_STATE = 16;
const MAX_VERSION = 24;
const AUTH_CACHE_SECONDS = 43200;     // a verified Discord token is trusted for 12 h

// --- request ---------------------------------------------------------------------------------

$op = $_GET['op'] ?? '';

$input = array_merge($_GET, read_json_body());

try {
    match ($op) {
        'list' => respond_ok(['rooms' => list_rooms()]),
        'register' => op_register($input),
        'heartbeat' => op_heartbeat($input),
        'update' => op_update($input),
        'unregister' => op_unregister($input),
        'whisper' => op_whisper($input),
        'whispers' => op_whispers($input),
        'signal-put' => op_signal($input),
        'signal-get' => op_signal_get($input),
        'join-request' => op_join_request($input),
        'tickets' => op_tickets($input),
        'ticket-state' => op_ticket_state($input),
        'ticket-put' => op_signal_put($input),
        'ticket-get' => op_ticket_get($input),
        'verify' => op_verify($input),
        'remote-pair-start' => op_remote_pair_start($input),
        'remote-pair-status' => op_remote_pair_status($input),
        'remote-pair-redeem' => op_remote_pair_redeem($input),
        'remote-heartbeat' => op_remote_heartbeat($input),
        'remote-report' => op_remote_report($input),
        'remote-attach' => op_remote_attach($input),
        'remote-list' => op_remote_list($input),
        'remote-connect' => op_remote_connect($input),
        'remote-disconnect' => op_remote_disconnect($input),
        'remote-set' => op_remote_set($input),
        'remote-forget' => op_remote_forget($input),
        'remote-unpair' => op_remote_unpair($input),
        default => respond_error('unknown op'),
    };
} catch (Throwable $e) {
    http_response_code(500);
    echo json_encode(['ok' => false, 'error' => 'backend error']);
    error_log('tixl-api: ' . $e->getMessage() . ' @ ' . basename($e->getFile()) . ':' . $e->getLine());
}

// --- storage ---------------------------------------------------------------------------------

function db(): PDO
{
    static $pdo = null;
    if ($pdo !== null) {
        return $pdo;
    }

    // The database lives OUTSIDE the web root (a sibling of www/), because dotfile-based
    // protection is not honoured on this host: anything under the web root is downloadable.
    // Falls back to ./data when the parent is not writable (local development).
    $dir = dirname(dirname(__DIR__)) . '/tixl-data';
    if (!is_dir($dir) && !@mkdir($dir, 0775, true) && !is_dir($dir)) {
        $dir = __DIR__ . '/data';
    }

    if (!is_dir($dir) && !@mkdir($dir, 0775, true) && !is_dir($dir)) {
        throw new RuntimeException('cannot create the data folder (permissions?)');
    }

    $pdo = new PDO('sqlite:' . $dir . '/collab.sqlite', null, null, [
        PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
    ]);
    $pdo->exec('PRAGMA journal_mode = WAL');
    $pdo->exec('PRAGMA busy_timeout = 3000');
    $pdo->exec('CREATE TABLE IF NOT EXISTS rooms (
        room TEXT PRIMARY KEY,
        key_hash TEXT NOT NULL,
        relay TEXT NOT NULL DEFAULT "",
        title TEXT NOT NULL DEFAULT "",
        purpose TEXT NOT NULL DEFAULT "",
        project TEXT NOT NULL DEFAULT "",
        host TEXT NOT NULL DEFAULT "",
        lang TEXT NOT NULL DEFAULT "",
        pwd INTEGER NOT NULL DEFAULT 0,
        listed INTEGER NOT NULL DEFAULT 0,
        members INTEGER NOT NULL DEFAULT 1,
        max_members INTEGER NOT NULL DEFAULT 18,
        whispers TEXT NOT NULL DEFAULT "[]",
        signals TEXT NOT NULL DEFAULT "[]",
        updated INTEGER NOT NULL DEFAULT 0
    )');

    // The migration guard: any expected column a database lacks gets added in place.
    $columns = $pdo->query('PRAGMA table_info(rooms)')->fetchAll(PDO::FETCH_COLUMN, 1);
    if (!in_array('relay', $columns, true)) {
        $pdo->exec('ALTER TABLE rooms ADD COLUMN relay TEXT NOT NULL DEFAULT ""');
    }
    if (!in_array('tickets', $columns, true)) {
        $pdo->exec('ALTER TABLE rooms ADD COLUMN tickets TEXT NOT NULL DEFAULT "[]"');
    }

    // Party-table columns: the host's platform, the account's region, and whether participants
    // may edit (the party's type in the matching list).
    if (!in_array('os', $columns, true)) {
        $pdo->exec('ALTER TABLE rooms ADD COLUMN os TEXT NOT NULL DEFAULT ""');
    }
    if (!in_array('region', $columns, true)) {
        $pdo->exec('ALTER TABLE rooms ADD COLUMN region TEXT NOT NULL DEFAULT ""');
    }
    if (!in_array('editable', $columns, true)) {
        $pdo->exec('ALTER TABLE rooms ADD COLUMN editable INTEGER NOT NULL DEFAULT 1');
    }

    // Remote management: paired installations (owner side), one-shot pairing codes, and a short
    // cache of Discord token checks so polling the remote list does not hammer Discord.
    $pdo->exec('CREATE TABLE IF NOT EXISTS remote_devices (
        id TEXT PRIMARY KEY,
        secret_hash TEXT NOT NULL,
        owner_id TEXT NOT NULL,
        owner_name TEXT NOT NULL DEFAULT "",
        name TEXT NOT NULL DEFAULT "",
        project TEXT NOT NULL DEFAULT "",
        version TEXT NOT NULL DEFAULT "",
        enabled INTEGER NOT NULL DEFAULT 1,
        paired_at INTEGER NOT NULL DEFAULT 0,
        last_seen INTEGER NOT NULL DEFAULT 0,
        pending TEXT NOT NULL DEFAULT "",
        pending_id TEXT NOT NULL DEFAULT "",
        session_claim TEXT NOT NULL DEFAULT "",
        room TEXT NOT NULL DEFAULT "",
        state TEXT NOT NULL DEFAULT "idle"
    )');
    $pdo->exec('CREATE TABLE IF NOT EXISTS pair_codes (
        code TEXT PRIMARY KEY,
        owner_id TEXT NOT NULL,
        owner_name TEXT NOT NULL DEFAULT "",
        created INTEGER NOT NULL,
        expires INTEGER NOT NULL,
        attempts INTEGER NOT NULL DEFAULT 0,
        device_id TEXT NOT NULL DEFAULT ""
    )');
    $pdo->exec('CREATE TABLE IF NOT EXISTS auth_cache (
        token_hash TEXT PRIMARY KEY,
        owner_id TEXT NOT NULL,
        owner_name TEXT NOT NULL DEFAULT "",
        lang TEXT NOT NULL DEFAULT "",
        expires INTEGER NOT NULL
    )');
    $pdo->exec('CREATE TABLE IF NOT EXISTS pair_attempts (
        at INTEGER NOT NULL,
        ip TEXT NOT NULL DEFAULT ""
    )');

    return $pdo;
}

// --- helpers ---------------------------------------------------------------------------------

/** Trims, cuts to length and drops control characters; these strings end up in other people's UI. */
function clean(string $value, int $max): string
{
    $cleaned = trim(preg_replace('/[\x00-\x1F\x7F]/u', '', $value) ?? '');
    return mb_substr($cleaned, 0, $max);
}

function text_field(array $input, string $name, int $max): string
{
    return clean((string)($input[$name] ?? ''), $max);
}

function room_code(array $input): string
{
    $code = strtoupper(preg_replace('/[^A-Za-z0-9]/', '', (string)($input['room'] ?? '')) ?? '');
    return substr($code, 0, MAX_ROOM_CODE);
}

/** A one-shot hex token that ties a join ticket to a remote-management connect request. */
function claim_code(array $input): string
{
    return substr(preg_replace('/[^a-f0-9]/', '', strtolower((string)($input['claim'] ?? ''))) ?? '', 0, 32);
}

/** The 6 digits a human types on the installation; spaces and dashes are tolerated. */
function pair_code(array $input): string
{
    $digits = preg_replace('/[^0-9]/', '', (string)($input['code'] ?? '')) ?? '';
    return substr($digits, 0, 6);
}

/** SDP needs its line breaks; strips everything else that is not printable or a newline. */
function sdp_clean(string $value): string
{
    $value = str_replace("\r\n", "\n", $value);
    $value = preg_replace('/[\x00-\x09\x0B-\x1F\x7F]/', '', $value) ?? '';
    return trim($value);
}

function key_hash(string $key): string
{
    return hash('sha256', $key);
}

function key_matches(array $room, string $key): bool
{
    return $key !== '' && hash_equals((string)$room['key_hash'], key_hash($key));
}

function respond_ok(array $data = []): never
{
    echo json_encode(['ok' => true, ...$data], JSON_UNESCAPED_UNICODE);
    exit;
}

function respond_error(string $message, int $status = 200): never
{
    http_response_code($status);
    echo json_encode(['ok' => false, 'error' => $message], JSON_UNESCAPED_UNICODE);
    exit;
}

function read_json_body(): array
{
    $raw = file_get_contents('php://input') ?: '';
    if ($raw === '') {
        return [];
    }
    try {
        $decoded = json_decode($raw, true, 32);
        return is_array($decoded) ? $decoded : [];
    } catch (Throwable) {
        return [];
    }
}

function load_room(string $code): ?array
{
    if ($code === '') {
        return null;
    }
    $stmt = db()->prepare('SELECT * FROM rooms WHERE room = ?');
    $stmt->execute([$code]);
    $room = $stmt->fetch(PDO::FETCH_ASSOC);
    return $room === false ? null : $room;
}

function delete_stale_rooms(): void
{
    db()->prepare('DELETE FROM rooms WHERE updated < ?')
        ->execute([time() - ROOM_TTL_SECONDS]);
}

function decode_list(string $json, int $maxEntries): array
{
    $decoded = json_decode($json, true);
    return is_array($decoded) ? array_slice($decoded, -$maxEntries) : [];
}

// --- ops -------------------------------------------------------------------------------------

function op_register(array $input): void
{
    $room = room_code($input);
    $key = (string)($input['key'] ?? '');
    if ($room === '' || $key === '') {
        respond_error('room and key required');
    }

    delete_stale_rooms();
    if ((int)db()->query('SELECT COUNT(*) FROM rooms')->fetchColumn() >= MAX_ROOMS) {
        respond_error('the session list is full');
    }

    // A re-register of a live room with the same key is the host coming back (reconnect);
    // without the key it is a code collision and the caller gets a fresh code elsewhere.
    $existing = load_room($room);
    if ($existing !== null && !key_matches($existing, $key)) {
        respond_error('room code taken');
    }

    $relay = clean((string)($input['relay'] ?? ''), 64);
        if ($relay !== '' && !preg_match('/^[a-zA-Z0-9.\-]+:[0-9]{1,5}$/', $relay)) {
            respond_error('relay must be host:port');
        }

        // Fields the caller leaves out must not wipe what the room already has (a reconnecting
        // host sends a partial hello): the update merges, the insert fills defaults.
        $pwd = isset($input['pwd']) ? (!empty($input['pwd']) ? 1 : 0) : null;
        $listed = isset($input['listed']) ? (!empty($input['listed']) ? 1 : 0) : null;
        $members = isset($input['members']) ? max(1, (int)$input['members']) : null;
        $maxMembers = isset($input['max']) ? max(2, (int)$input['max']) : null;

        if ($existing !== null) {
            $stmt = db()->prepare('UPDATE rooms SET key_hash = ?, relay = CASE WHEN ? = "" THEN relay ELSE ? END,
                             title = CASE WHEN ? = "" THEN title ELSE ? END,
                             purpose = CASE WHEN ? = "" THEN purpose ELSE ? END,
                             project = CASE WHEN ? = "" THEN project ELSE ? END,
                             host = CASE WHEN ? = "" THEN host ELSE ? END,
                             lang = CASE WHEN ? = "" THEN lang ELSE ? END,
                             os = CASE WHEN ? = "" THEN os ELSE ? END,
                             region = CASE WHEN ? = "" THEN region ELSE ? END,
                             editable = COALESCE(?, editable),
                             pwd = COALESCE(?, pwd),
                             listed = COALESCE(?, listed),
                             members = COALESCE(?, members),
                             max_members = COALESCE(?, max_members),
                             updated = ?
                           WHERE room = ?');
            $stmt->execute([
                key_hash($key),
                $relay, $relay,
                text_field($input, 'title', MAX_TITLE), text_field($input, 'title', MAX_TITLE),
                text_field($input, 'purpose', MAX_PURPOSE), text_field($input, 'purpose', MAX_PURPOSE),
                text_field($input, 'project', MAX_PROJECT), text_field($input, 'project', MAX_PROJECT),
                text_field($input, 'host', MAX_NAME), text_field($input, 'host', MAX_NAME),
                text_field($input, 'lang', MAX_LANG), text_field($input, 'lang', MAX_LANG),
                text_field($input, 'os', 12), text_field($input, 'os', 12),
                text_field($input, 'region', 8), text_field($input, 'region', 8),
                isset($input['editable']) ? (!empty($input['editable']) ? 1 : 0) : null,
                $pwd, $listed, $members, $maxMembers,
                time(),
                $room,
            ]);
            respond_ok(['room' => $room]);
        }

        $stmt = db()->prepare('INSERT INTO rooms (room, key_hash, relay, title, purpose, project, host, lang, os, region, editable, pwd, listed, members, max_members, updated)
                           VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)');
    $stmt->execute([
        $room,
        key_hash($key),
        $relay,
        text_field($input, 'title', MAX_TITLE),
        text_field($input, 'purpose', MAX_PURPOSE),
        text_field($input, 'project', MAX_PROJECT),
        text_field($input, 'host', MAX_NAME),
        text_field($input, 'lang', MAX_LANG),
        text_field($input, 'os', 12),
        text_field($input, 'region', 8),
        !empty($input['editable']) ? 1 : 0,
        $pwd ?? 0,
        $listed ?? 0,
        $members ?? 1,
        $maxMembers ?? 18,
        time(),
    ]);

    respond_ok(['room' => $room]);
}

function op_heartbeat(array $input): void
{
    $room = room_code($input);
    $found = load_room($room);
    if ($found === null) {
        respond_error('room not found');
    }
    if (!key_matches($found, (string)($input['key'] ?? ''))) {
        respond_error('not the host');
    }

    // The host also reports spectator-mode changes with its heartbeat, so the matching list
    // reflects "controllable" vs "spectating" without a separate call.
    if (isset($input['editable'])) {
        db()->prepare('UPDATE rooms SET updated = ?, members = ?, editable = ? WHERE room = ?')
            ->execute([time(), max(1, (int)($input['members'] ?? 1)), !empty($input['editable']) ? 1 : 0, $room]);
    } else {
        db()->prepare('UPDATE rooms SET updated = ?, members = ? WHERE room = ?')
            ->execute([time(), max(1, (int)($input['members'] ?? 1)), $room]);
    }

    respond_ok();
}

function op_update(array $input): void
{
    $room = room_code($input);
    $found = load_room($room);
    if ($found === null) {
        respond_error('room not found');
    }
    if (!key_matches($found, (string)($input['key'] ?? ''))) {
        respond_error('not the host');
    }

    $title = text_field($input, 'title', MAX_TITLE);
    $purpose = text_field($input, 'purpose', MAX_PURPOSE);
    $project = text_field($input, 'project', MAX_PROJECT);
    $editable = isset($input['editable']) ? (!empty($input['editable']) ? 1 : 0) : null;
    $stmt = db()->prepare('UPDATE rooms SET title = CASE WHEN ? = "" THEN title ELSE ? END,
                                              purpose = CASE WHEN ? = "" THEN purpose ELSE ? END,
                                              project = CASE WHEN ? = "" THEN project ELSE ? END,
                                              editable = COALESCE(?, editable),
                                              listed = ?,
                                              updated = ?
                           WHERE room = ?');
    $stmt->execute([$title, $title, $purpose, $purpose, $project, $project,
                    $editable,
                    !empty($input['listed']) ? 1 : 0, time(), $room]);
    respond_ok();
}

function op_unregister(array $input): void
{
    $room = room_code($input);
    $found = load_room($room);
    if ($found === null) {
        respond_ok();
    }
    if (!key_matches($found, (string)($input['key'] ?? ''))) {
        respond_error('not the host');
    }

    db()->prepare('DELETE FROM rooms WHERE room = ?')->execute([$room]);
    respond_ok();
}

function list_rooms(): array
{
    delete_stale_rooms();
    $rows = db()->query('SELECT room, relay, title, purpose, project, host, lang, os, region, editable, pwd, members, max_members, updated
                         FROM rooms WHERE listed = 1 ORDER BY updated DESC LIMIT 200')->fetchAll(PDO::FETCH_ASSOC);
    $rooms = [];
    foreach ($rows as $row) {
        $rooms[] = [
            'code' => $row['room'],
            'relay' => $row['relay'],
            'title' => $row['title'],
            'purpose' => $row['purpose'],
            'project' => $row['project'],
            'host' => $row['host'],
            'lang' => $row['lang'],
            'os' => $row['os'],
            'region' => $row['region'],
            'editable' => (bool)$row['editable'],
            'pwd' => (bool)$row['pwd'],
            'members' => (int)$row['members'],
            'max' => (int)$row['max_members'],
            'age' => time() - (int)$row['updated'],
        ];
    }
    return $rooms;
}

function op_whisper(array $input): void
{
    $room = room_code($input);
    $found = load_room($room);
    if ($found === null) {
        respond_error('room not found');
    }

    $key = (string)($input['key'] ?? '');
    $text = text_field($input, 'text', MAX_TEXT);
    if ($text === '') {
        respond_error('text required');
    }

    // With the room key this is the host replying; the reply is visible to the visitor it names.
    // Without it this is a visitor whispering, which carries the visitor's name.
    $isHostReply = false;
    $from = '';
    $to = '';
    if ($key !== '') {
        if (!key_matches($found, $key)) {
            respond_error('not the host');
        }
        $isHostReply = true;
        $to = text_field($input, 'to', MAX_NAME);
        if ($to === '') {
            respond_error('to required');
        }
    } else {
        $from = text_field($input, 'name', MAX_NAME);
        if ($from === '') {
            respond_error('name required');
        }
    }

    append_whisper($room, ['from' => $isHostReply ? $found['host'] : $from,
                           'text' => $text,
                           'host' => $isHostReply,
                           'to' => $to,
                           'at' => time()]);
    respond_ok();
}

function op_whispers(array $input): void
{
    $room = room_code($input);
    $found = load_room($room);
    if ($found === null) {
        respond_error('room not found');
    }

    $since = (int)($input['since'] ?? 0);
    $whispers = decode_list((string)$found['whispers'], MAX_WHISPERS_PER_ROOM);

    // The host reads everything with its key; a visitor reads its own thread by name.
    $key = (string)($input['key'] ?? '');
    $name = text_field($input, 'name', MAX_NAME);
    if ($key !== '') {
        if (!key_matches($found, $key)) {
            respond_error('not the host');
        }
        respond_ok(['whispers' => array_values(array_filter($whispers, fn($w) => $w['at'] > $since))]);
    }

    if ($name === '') {
        respond_error('key or name required');
    }

    respond_ok(['whispers' => array_values(array_filter(
        $whispers,
        fn($w) => $w['at'] > $since && ($w['host'] === true ? ($w['to'] ?? '') === $name : $w['from'] === $name)
    ))]);
}

function append_whisper(string $room, array $entry): void
{
    with_room_lock(function () use ($room, $entry) {
        $fresh = load_room($room);
        $whispers = decode_list((string)($fresh['whispers'] ?? '[]'), MAX_WHISPERS_PER_ROOM);
        $whispers[] = $entry;
        if (count($whispers) > MAX_WHISPERS_PER_ROOM) {
            $whispers = array_slice($whispers, -MAX_WHISPERS_PER_ROOM);
        }

        db()->prepare('UPDATE rooms SET whispers = ? WHERE room = ?')
            ->execute([json_encode($whispers, JSON_UNESCAPED_UNICODE), $room]);
    });
}

// --- relay-free WebRTC signaling -------------------------------------------------------------

/** Puts an offer or answer. The host signs its offers with the room key; a guest's answer carries a nonce. */
function op_signal(array $input): void
{
    $room = room_code($input);
    $found = load_room($room);
    if ($found === null) {
        respond_error('room not found');
    }

    $kind = (string)($input['kind'] ?? '');
    if ($kind !== 'offer' && $kind !== 'answer') {
        respond_error('kind must be offer or answer');
    }

    $sdp = mb_substr(sdp_clean((string)($input['sdp'] ?? '')), 0, 16384);
    if ($sdp === '') {
        respond_error('sdp required');
    }

    if ($kind === 'offer') {
        if (!key_matches($found, (string)($input['key'] ?? ''))) {
            respond_error('not the host');
        }
        $nonce = bin2hex(random_bytes(8));
        // Append inside the write lock: a parallel offer must not be lost.
        with_room_lock(function () use ($room, $sdp, $nonce) {
            // Re-read inside the write lock: a parallel post may have appended since the load above.
            $fresh = load_room($room);
            $signals = decode_list((string)$fresh['signals'], 16);
            $signals[] = ['kind' => 'offer', 'sdp' => $sdp, 'nonce' => $nonce, 'at' => time()];
            save_signals($room, $signals);
        });
        respond_ok(['nonce' => $nonce]);
    }

    // An answer goes to exactly one offer's nonce, so no other visitor can read it.
    $nonce = substr(preg_replace('/[^a-f0-9]/', '', (string)($input['nonce'] ?? '')) ?? '', 0, 16);
    if ($nonce === '') {
        respond_error('nonce required');
    }

    $answered = false;
    with_room_lock(function () use ($room, $nonce, $sdp, &$answered) {
        // Re-read inside the write lock: a parallel answer may have written since the load above.
        $fresh = load_room($room);
        $signals = decode_list((string)$fresh['signals'], 16);
        foreach ($signals as $index => $signal) {
            if ($signal['kind'] === 'offer' && $signal['nonce'] === $nonce) {
                // Write through the index: a foreach copy would silently drop the answer.
                $signals[$index]['answer'] = $sdp;
                $signals[$index]['answered'] = time();
                save_signals($room, $signals);
                $answered = true;
                return;
            }
        }
    });

    if (!$answered) {
        respond_error('offer not found');
    }
    respond_ok();
}

function save_signals(string $room, array $signals): void
{
    db()->prepare('UPDATE rooms SET signals = ?, updated = ? WHERE room = ?')
        ->execute([json_encode($signals, JSON_UNESCAPED_UNICODE), time(), $room]);
}

function op_signal_get(array $input): void
{
    $room = room_code($input);
    $found = load_room($room);
    if ($found === null) {
        respond_error('room not found');
    }

    $nonce = substr(preg_replace('/[^a-f0-9]/', '', (string)($input['nonce'] ?? '')) ?? '', 0, 16);
    $signals = decode_list((string)$found['signals'], 16);
    foreach ($signals as $signal) {
        if ($signal['nonce'] === $nonce) {
            respond_ok(['signal' => [
                'kind' => $signal['kind'],
                'sdp' => $signal['sdp'],
                'answer' => $signal['answer'] ?? null,
            ]]);
        }
    }

    respond_error('signal not found');
}

// --- join tickets: the relay-free session rendezvous ------------------------------------------

// A ticket is a two-party signal mailbox: the guest creates it with join-request, the host
// fills it with offer/candidates, the guest answers into it, and both sides poll until the
// WebRTC data channel takes over. Messages are the editor's own signal JSON
// ({t:"offer"|"answer"|"candidate", ...}) passed through unmodified.

function op_join_request(array $input): void
{
    $room = room_code($input);
    $found = load_room($room);
    if ($found === null) {
        respond_error('room not found');
    }

    $name = text_field($input, 'name', MAX_NAME);
    if ($name === '') {
        respond_error('name required');
    }

    $ticketId = bin2hex(random_bytes(8));
    with_room_lock(function () use ($room, $name, $input, $ticketId) {
        $fresh = load_room($room);
        $tickets = decode_list((string)($fresh['tickets'] ?? '[]'), MAX_TICKETS_PER_ROOM);
        $tickets[] = ['id' => $ticketId,
                      'name' => $name,
                      'lang' => text_field($input, 'lang', MAX_LANG),
                      'claim' => claim_code($input),
                      'state' => 'pending',
                      'at' => time(),
                      'msgs' => []];
        if (count($tickets) > MAX_TICKETS_PER_ROOM) {
            $tickets = array_slice($tickets, -MAX_TICKETS_PER_ROOM);
        }

        save_tickets($room, $tickets);
    });

    respond_ok(['ticket' => $ticketId]);
}

/** Host: sets a join ticket's state (approved lets the negotiation start; rejected ends it). */
function op_ticket_state(array $input): void
{
    $room = room_code($input);
    $found = load_room($room);
    if ($found === null) {
        respond_error('room not found');
    }
    if (!key_matches($found, (string)($input['key'] ?? ''))) {
        respond_error('not the host');
    }

    $ticketId = substr(preg_replace('/[^a-f0-9]/', '', (string)($input['ticket'] ?? '')) ?? '', 0, 16);
    $state = (string)($input['state'] ?? '');
    if (!in_array($state, ['approved', 'rejected'], true)) {
        respond_error('state must be approved or rejected');
    }

    $tickets = decode_list((string)$found['tickets'], MAX_TICKETS_PER_ROOM);
    foreach ($tickets as $index => $ticket) {
        if ($ticket['id'] === $ticketId) {
            with_room_lock(function () use ($room, $ticketId, $state) {
                $fresh = load_room($room);
                $current = decode_list((string)($fresh['tickets'] ?? '[]'), MAX_TICKETS_PER_ROOM);
                foreach ($current as $i => $t) {
                    if ($t['id'] === $ticketId) {
                        $current[$i]['state'] = $state;
                        $current[$i]['answered'] = time();
                        save_tickets($room, $current);
                        return;
                    }
                }
            });

            respond_ok();
        }
    }

    respond_error('ticket not found');
}

function op_tickets(array $input): void
{
    $room = room_code($input);
    $found = load_room($room);
    if ($found === null) {
        respond_error('room not found');
    }
    if (!key_matches($found, (string)($input['key'] ?? ''))) {
        respond_error('not the host');
    }

    $tickets = decode_list((string)$found['tickets'], MAX_TICKETS_PER_ROOM);
    $out = [];
    foreach ($tickets as $ticket) {
        $out[] = ['id' => $ticket['id'],
                  'name' => $ticket['name'],
                  'lang' => $ticket['lang'] ?? '',
                  'claim' => $ticket['claim'] ?? '',
                  'state' => $ticket['state'] ?? 'pending',
                  'at' => $ticket['at'],
                  'msgs' => count($ticket['msgs'] ?? [])];
    }

    respond_ok(['tickets' => $out]);
}

function op_signal_put(array $input): void
{
    $room = room_code($input);
    $found = load_room($room);
    if ($found === null) {
        respond_error('room not found');
    }

    $ticketId = substr(preg_replace('/[^a-f0-9]/', '', (string)($input['ticket'] ?? '')) ?? '', 0, 16);
    if ($ticketId === '') {
        respond_error('ticket required');
    }

    $message = $input['message'] ?? null;
    if (!is_array($message)) {
        respond_error('message required');
    }

    $type = (string)($message['t'] ?? '');
    if (!in_array($type, ['offer', 'answer', 'ice'], true)) {
        respond_error('t must be offer, answer or ice');
    }

    // Offer and answer carry SDP (line breaks preserved); candidates carry candidate/mid.
    foreach (['sdp' => 16384, 'candidate' => 512, 'mid' => 16] as $field => $max) {
        if (isset($message[$field])) {
            $message[$field] = $field === 'sdp' ? sdp_clean((string)$message[$field]) : clean((string)$message[$field], $max);
        }
    }

    // The host signs its messages with the room key; a guest signs with its ticket id.
    $isHost = key_matches($found, (string)($input['key'] ?? ''));

    $foundTicket = false;
    $sawCount = 0;
    with_room_lock(function () use ($room, $ticketId, $isHost, $message, $type, &$foundTicket, &$sawCount) {
        // Re-read inside the write lock: a parallel post may have appended since the load above.
        $fresh = load_room($room);
        $tickets = decode_list((string)($fresh['tickets'] ?? '[]'), MAX_TICKETS_PER_ROOM);
        foreach ($tickets as $index => $ticket) {
            if ($ticket['id'] !== $ticketId) {
                continue;
            }

            $msgs = $ticket['msgs'] ?? [];
            $sawCount = count($msgs);
            $msgs[] = ['from' => $isHost ? 'host' : 'guest',
                       't' => $type,
                       'sdp' => $message['sdp'] ?? '',
                       'candidate' => $message['candidate'] ?? '',
                       'mid' => $message['mid'] ?? '',
                       'at' => time()];
            if (count($msgs) > MAX_MSGS_PER_TICKET) {
                $msgs = array_slice($msgs, -MAX_MSGS_PER_TICKET);
            }

            $tickets[$index]['msgs'] = $msgs;
            save_tickets($room, $tickets);
            $foundTicket = true;
            return;
        }
    });

    if (!$foundTicket) {
        respond_error('ticket not found');
    }

    respond_ok(['from' => $isHost ? 'host' : 'guest', 'saw' => $sawCount]);
}

function op_ticket_get(array $input): void
{
    $room = room_code($input);
    $found = load_room($room);
    if ($found === null) {
        respond_error('room not found');
    }

    $ticketId = substr(preg_replace('/[^a-f0-9]/', '', (string)($input['ticket'] ?? '')) ?? '', 0, 16);
    $since = (int)($input['since'] ?? 0);
    $tickets = decode_list((string)$found['tickets'], MAX_TICKETS_PER_ROOM);
    foreach ($tickets as $ticket) {
        if ($ticket['id'] !== $ticketId) {
            continue;
        }

        $msgs = array_slice($ticket['msgs'] ?? [], $since);
        respond_ok(['msgs' => $msgs, 'state' => $ticket['state'] ?? 'pending']);
    }

    respond_error('ticket not found');
}

function save_tickets(string $room, array $tickets): void
{
    db()->prepare('UPDATE rooms SET tickets = ?, updated = ? WHERE room = ?')
        ->execute([json_encode($tickets, JSON_UNESCAPED_UNICODE), time(), $room]);
}

/**
 * Serialises a read-modify-write on one room's JSON columns. A host posts offer and ICE
 * candidates in parallel; without the write lock each request read the ticket before the
 * others appended and the last writer silently dropped the rest (the offer vanished).
 */
function with_room_lock(callable $mutate): void
{
    $pdo = db();
    $pdo->exec('BEGIN IMMEDIATE');
    try
    {
        $mutate();
        $pdo->exec('COMMIT');
    }
    catch (Throwable $e)
    {
        $pdo->exec('ROLLBACK');
        throw $e;
    }
}

// --- Discord identity (the account layer) ----------------------------------------------------

/** Proxies Discord's /users/@me so a peer never sees another peer's token; the backend keeps the trust. */
function op_verify(array $input): void
{
    $token = text_field($input, 'token', 512);
    if ($token === '') {
        respond_error('token required');
    }

    $user = fetch_discord_user($token);
    if ($user === null) {
        respond_ok(['verified' => false]);
    }

    respond_ok([
        'verified' => true,
        'id' => (string)$user['id'],
        'name' => clean($user['global_name'] ?? ($user['username'] ?? ''), MAX_NAME),
        'lang' => clean($user['locale'] ?? '', MAX_LANG),
        'avatar' => clean((string)($user['avatar'] ?? ''), 64),
    ]);
}

/** Verifies a Discord access token against Discord; null when the token is not valid. */
function fetch_discord_user(string $token): ?array
{
    $context = stream_context_create(['http' => [
        'method' => 'GET',
        'header' => "Authorization: Bearer $token\r\nUser-Agent: TiXLCollab\r\n",
        'timeout' => 5,
        'ignore_errors' => true,
    ]]);
    $body = @file_get_contents('https://discord.com/api/v10/users/@me', false, $context);
    if ($body === false) {
        return null;
    }

    $user = json_decode($body, true);
    return is_array($user) && isset($user['id']) ? $user : null;
}

// --- Remote management: pairing, presence and connect requests -------------------------------
//
// Roles: an owner (a Discord account, the controller) pairs unattended TiXL installations once
// and then reconnects whenever they are online. Pairing is a 6-digit code the owner shows and
// the installation redeems; redemption issues a long-lived device secret, so the installation
// never needs a Discord login. Presence is the device's heartbeat; a connect request is a
// one-shot command on the device row, acknowledged by the next heartbeat, and the room code is
// reported back so the controller can join over the same relay-free WebRTC path.

/** The Discord account behind a controller request; verified once and cached to keep polling cheap. */
function discord_identity(array $input): array
{
    $token = text_field($input, 'token', 512);
    if ($token === '') {
        respond_error('Discord login required');
    }

    $hash = hash('sha256', $token);
    $stmt = db()->prepare('SELECT owner_id, owner_name, lang FROM auth_cache WHERE token_hash = ? AND expires > ?');
    $stmt->execute([$hash, time()]);
    $cached = $stmt->fetch(PDO::FETCH_ASSOC);
    if ($cached !== false) {
        return $cached;
    }

    $user = fetch_discord_user($token);
    if ($user === null) {
        respond_error('Discord login could not be verified - log in again');
    }

    $identity = [
        'owner_id' => (string)$user['id'],
        'owner_name' => clean($user['global_name'] ?? ($user['username'] ?? ''), MAX_NAME),
        'lang' => clean($user['locale'] ?? '', MAX_LANG),
    ];
    db()->prepare('INSERT OR REPLACE INTO auth_cache (token_hash, owner_id, owner_name, lang, expires) VALUES (?, ?, ?, ?, ?)')
        ->execute([$hash, $identity['owner_id'], $identity['owner_name'], $identity['lang'], time() + AUTH_CACHE_SECONDS]);
    return $identity;
}

function load_device(string $id): ?array
{
    if ($id === '') {
        return null;
    }

    $stmt = db()->prepare('SELECT * FROM remote_devices WHERE id = ?');
    $stmt->execute([$id]);
    $device = $stmt->fetch(PDO::FETCH_ASSOC);
    return $device === false ? null : $device;
}

/** Authenticates an installation by its device id and secret (no Discord involved). */
function require_device(array $input): array
{
    $id = substr(preg_replace('/[^a-f0-9]/', '', strtolower((string)($input['device'] ?? ''))) ?? '', 0, 32);
    $secret = (string)($input['secret'] ?? '');
    $device = load_device($id);
    if ($device === null || $secret === '' || !hash_equals($device['secret_hash'], key_hash($secret))) {
        respond_error('device not found');
    }

    return $device;
}

/** Authenticates a controller and returns only a device the controller owns. */
function require_owned_device(array $input, array $owner): array
{
    $id = substr(preg_replace('/[^a-f0-9]/', '', strtolower((string)($input['device'] ?? ''))) ?? '', 0, 32);
    $device = load_device($id);
    if ($device === null || !hash_equals($device['owner_id'], $owner['owner_id'])) {
        respond_error('device not found');
    }

    return $device;
}

/** The pending command on a device row as an array; null when there is none. */
function device_command(array $device): ?array
{
    if (($device['pending'] ?? '') === '') {
        return null;
    }

    $command = json_decode((string)$device['pending'], true);
    return is_array($command) ? $command : null;
}

/** Owner: starts a pairing and shows the 6-digit code; a new code replaces the previous one. */
function op_remote_pair_start(array $input): void
{
    $owner = discord_identity($input);
    $now = time();
    db()->prepare('DELETE FROM pair_codes WHERE expires < ?')->execute([$now]);
    db()->prepare('DELETE FROM pair_codes WHERE owner_id = ?')->execute([$owner['owner_id']]);

    $code = new_pair_code();
    db()->prepare('INSERT INTO pair_codes (code, owner_id, owner_name, created, expires) VALUES (?, ?, ?, ?, ?)')
        ->execute([$code, $owner['owner_id'], $owner['owner_name'], $now, $now + PAIR_TTL_SECONDS]);
    respond_ok(['code' => $code, 'expires' => $now + PAIR_TTL_SECONDS, 'owner' => $owner['owner_name']]);
}

function new_pair_code(): string
{
    for ($attempt = 0; $attempt < 60; $attempt++) {
        $code = str_pad((string)random_int(0, 999999), 6, '0', STR_PAD_LEFT);
        $stmt = db()->prepare('SELECT 1 FROM pair_codes WHERE code = ?');
        $stmt->execute([$code]);
        if ($stmt->fetchColumn() === false) {
            return $code;
        }
    }

    respond_error('could not create a pairing code');
}

/** Owner: polls the code it is displaying until an installation redeems it. */
function op_remote_pair_status(array $input): void
{
    $owner = discord_identity($input);
    $code = pair_code($input);
    if ($code === '') {
        respond_error('code required');
    }

    $stmt = db()->prepare('SELECT * FROM pair_codes WHERE code = ?');
    $stmt->execute([$code]);
    $row = $stmt->fetch(PDO::FETCH_ASSOC);
    if ($row === false || !hash_equals($row['owner_id'], $owner['owner_id'])) {
        respond_error('pairing code not found');
    }

    if ($row['device_id'] !== '') {
        $device = load_device((string)$row['device_id']);
        if ($device !== null) {
            respond_ok(['state' => 'paired',
                        'code' => $code,
                        'device' => ['id' => $device['id'],
                                     'name' => $device['name'],
                                     'project' => $device['project']]]);
        }
    }

    if ((int)$row['expires'] < time()) {
        respond_ok(['state' => 'expired', 'code' => $code]);
    }

    respond_ok(['state' => 'waiting', 'code' => $code, 'expires' => (int)$row['expires']]);
}

/** Installation: redeems the code shown on the controller and receives its long-lived secret. */
function op_remote_pair_redeem(array $input): void
{
    $now = time();
    db()->prepare('DELETE FROM pair_attempts WHERE at < ?')->execute([$now - PAIR_ATTEMPT_WINDOW]);
    $attempts = (int)db()->query('SELECT COUNT(*) FROM pair_attempts')->fetchColumn();
    if ($attempts >= PAIR_ATTEMPT_LIMIT) {
        respond_error('too many pairing attempts - try again later');
    }

    db()->prepare('INSERT INTO pair_attempts (at, ip) VALUES (?, ?)')
        ->execute([$now, substr((string)($_SERVER['REMOTE_ADDR'] ?? ''), 0, 64)]);

    $code = pair_code($input);
    if ($code === '') {
        respond_error('code required');
    }

    $stmt = db()->prepare('SELECT * FROM pair_codes WHERE code = ?');
    $stmt->execute([$code]);
    $row = $stmt->fetch(PDO::FETCH_ASSOC);
    if ($row === false) {
        respond_error('pairing code is not valid');
    }

    db()->prepare('UPDATE pair_codes SET attempts = attempts + 1 WHERE code = ?')->execute([$code]);
    if ((int)$row['attempts'] >= PAIR_MAX_ATTEMPTS) {
        db()->prepare('DELETE FROM pair_codes WHERE code = ?')->execute([$code]);
        respond_error('too many attempts on this code - generate a new one');
    }

    if ((int)$row['expires'] < $now) {
        db()->prepare('DELETE FROM pair_codes WHERE code = ?')->execute([$code]);
        respond_error('pairing code has expired');
    }

    if ($row['device_id'] !== '') {
        respond_error('this code has already been used');
    }

    $countStmt = db()->prepare('SELECT COUNT(*) FROM remote_devices WHERE owner_id = ?');
    $countStmt->execute([$row['owner_id']]);
    if ((int)$countStmt->fetchColumn() >= MAX_DEVICES) {
        respond_error('too many paired computers for this account');
    }

    $name = text_field($input, 'name', MAX_DEVICE_NAME);
    if ($name === '') {
        $name = 'TiXL';
    }

    $id = bin2hex(random_bytes(12));
    $secret = bin2hex(random_bytes(32));
    db()->prepare('INSERT INTO remote_devices (id, secret_hash, owner_id, owner_name, name, project, version,
                    enabled, paired_at, last_seen, state)
                   VALUES (?, ?, ?, ?, ?, ?, ?, 1, ?, ?, "idle")')
        ->execute([$id, key_hash($secret), $row['owner_id'], $row['owner_name'], $name,
                   text_field($input, 'project', MAX_DEVICE_PROJECT), text_field($input, 'version', MAX_VERSION),
                   $now, $now]);
    db()->prepare('UPDATE pair_codes SET device_id = ? WHERE code = ?')->execute([$id, $code]);

    respond_ok(['device' => $id, 'secret' => $secret, 'owner' => $row['owner_name'], 'name' => $name]);
}

/** Installation: presence plus the delivery of connect/disconnect commands. */
function op_remote_heartbeat(array $input): void
{
    $device = require_device($input);
    $now = time();

    $project = text_field($input, 'project', MAX_DEVICE_PROJECT);
    $version = text_field($input, 'version', MAX_VERSION);
    $state = text_field($input, 'state', MAX_DEVICE_STATE);
    if (!in_array($state, ['idle', 'busy'], true)) {
        $state = 'idle';
    }

    // An ack clears the command the installation acted on; without it the command is resent.
    $command = device_command($device);
    $ack = substr(preg_replace('/[^a-f0-9]/', '', strtolower((string)($input['ack'] ?? ''))) ?? '', 0, 32);
    if ($command !== null && $ack !== '' && hash_equals((string)$device['pending_id'], $ack)) {
        $command = null;
    }

    if ($command !== null && ($command['type'] ?? '') === 'connect'
        && $now - (int)($command['at'] ?? 0) > REMOTE_CONNECT_TTL) {
        $command = null;
    }

    // An idle installation has no session: a room remembered from an earlier one (e.g. the
    // installation restarted) would send its owner's controller into a dead party.
    if ($state === 'idle') {
        db()->prepare('UPDATE remote_devices SET project = ?, version = ?, last_seen = ?, state = ?,
                         pending = ?, pending_id = ?, room = "", session_claim = "" WHERE id = ?')
            ->execute([
                          $project,
                          $version !== '' ? $version : $device['version'],
                          $now,
                          $state,
                          $command === null ? '' : json_encode($command, JSON_UNESCAPED_UNICODE),
                          $command === null ? '' : $device['pending_id'],
                          $device['id'],
                      ]);
    } else {
        db()->prepare('UPDATE remote_devices SET project = ?, version = ?, last_seen = ?, state = ?,
                         pending = ?, pending_id = ? WHERE id = ?')
            ->execute([
                          $project,
                          $version !== '' ? $version : $device['version'],
                          $now,
                          $state,
                          $command === null ? '' : json_encode($command, JSON_UNESCAPED_UNICODE),
                          $command === null ? '' : $device['pending_id'],
                          $device['id'],
                      ]);
    }

    respond_ok(['enabled' => (bool)$device['enabled'],
                'owner' => $device['owner_name'],
                'command' => $command,
                'commandId' => $command === null ? '' : $device['pending_id']]);
}

/** Installation: reports the room it hosts (and when the controller has joined) or that it is idle. */
function op_remote_report(array $input): void
{
    $device = require_device($input);
    $state = text_field($input, 'state', MAX_DEVICE_STATE);
    if (!in_array($state, ['hosting', 'connected', 'idle', 'failed', 'busy'], true)) {
        respond_error('unknown state');
    }

    // Idle is allowed unconditionally: the installation is telling us the session is over.
    if ($state === 'idle') {
        db()->prepare('UPDATE remote_devices SET room = "", state = ?, session_claim = "", last_seen = ? WHERE id = ?')
            ->execute(['idle', time(), $device['id']]);
        respond_ok();
    }

    $claim = claim_code($input);
    $expected = (string)$device['session_claim'];
    if ($expected === '') {
        $command = device_command($device);
        if ($command !== null && ($command['type'] ?? '') === 'connect') {
            $expected = (string)($command['claim'] ?? '');
        }
    }

    if ($claim === '' || $expected === '' || !hash_equals($expected, $claim)) {
        respond_error('connect request does not match');
    }

    $room = room_code($input);
    db()->prepare('UPDATE remote_devices SET room = ?, state = ?, session_claim = ?, last_seen = ? WHERE id = ?')
        ->execute([$room, $state, $claim, time(), $device['id']]);
    respond_ok();
}

/** Controller: confirms it is in the remote session (the device then shows it as controlled). */
function op_remote_attach(array $input): void
{
    $owner = discord_identity($input);
    $device = require_owned_device($input, $owner);
    $claim = claim_code($input);
    if ($device['session_claim'] === '' || !hash_equals((string)$device['session_claim'], $claim)) {
        respond_error('connect request does not match');
    }

    db()->prepare('UPDATE remote_devices SET state = "connected", last_seen = ? WHERE id = ?')
        ->execute([time(), $device['id']]);
    respond_ok();
}

/** Controller: the paired computers of the logged-in account, with presence and session state. */
function op_remote_list(array $input): void
{
    $owner = discord_identity($input);
    $stmt = db()->prepare('SELECT * FROM remote_devices WHERE owner_id = ? ORDER BY paired_at DESC');
    $stmt->execute([$owner['owner_id']]);

    $now = time();
    $devices = [];
    foreach ($stmt->fetchAll(PDO::FETCH_ASSOC) as $device) {
        $claim = (string)$device['session_claim'];
        if ($claim === '') {
            $command = device_command($device);
            if ($command !== null && ($command['type'] ?? '') === 'connect') {
                $claim = (string)($command['claim'] ?? '');
            }
        }

        $devices[] = [
            'id' => $device['id'],
            'name' => $device['name'],
            'project' => $device['project'],
            'version' => $device['version'],
            'owner' => $device['owner_name'],
            'enabled' => (bool)$device['enabled'],
            'online' => (int)$device['last_seen'] > $now - REMOTE_ONLINE_SECONDS,
            'lastSeen' => (int)$device['last_seen'],
            'pairedAt' => (int)$device['paired_at'],
            'state' => $device['state'],
            'room' => $device['room'],
            'claim' => $claim,
        ];
    }

    respond_ok(['devices' => $devices]);
}

/** Controller: asks an online installation to host a managed session; returns the one-shot claim. */
function op_remote_connect(array $input): void
{
    $owner = discord_identity($input);
    $device = require_owned_device($input, $owner);
    if (!(bool)$device['enabled']) {
        respond_error('remote management is disabled on that computer');
    }

    if ((int)$device['last_seen'] <= time() - REMOTE_ONLINE_SECONDS) {
        respond_error('the remote TiXL is not online');
    }

    // Reuse a live request so a controller that restarted mid-connect can still join.
    $command = device_command($device);
    if ($command !== null && ($command['type'] ?? '') === 'connect'
        && time() - (int)($command['at'] ?? 0) <= REMOTE_CONNECT_TTL) {
        respond_ok(['claim' => $command['claim'], 'command' => $device['pending_id'], 'room' => $device['room']]);
    }

    // A remembered room is only reusable while it actually exists: a registration that
    // failed (e.g. rate limited) would otherwise be handed out forever.
    if ($device['session_claim'] !== '' && $device['room'] !== '' && load_room($device['room']) !== null) {
        respond_ok(['claim' => $device['session_claim'], 'room' => $device['room']]);
    }

    if ($device['session_claim'] !== '' || $device['room'] !== '') {
        db()->prepare('UPDATE remote_devices SET session_claim = "", room = "", state = "idle" WHERE id = ?')
            ->execute([$device['id']]);
    }

    $claim = bin2hex(random_bytes(8));
    $commandId = bin2hex(random_bytes(8));
    $payload = json_encode(['type' => 'connect', 'claim' => $claim, 'at' => time()], JSON_UNESCAPED_UNICODE);
    db()->prepare('UPDATE remote_devices SET pending = ?, pending_id = ?, room = "", state = "requested",
                     session_claim = "" WHERE id = ?')
        ->execute([$payload, $commandId, $device['id']]);
    respond_ok(['claim' => $claim, 'command' => $commandId, 'room' => '']);
}

/** Controller: ends the managed session; the installation leaves and reports idle. */
function op_remote_disconnect(array $input): void
{
    $owner = discord_identity($input);
    $device = require_owned_device($input, $owner);
    $commandId = bin2hex(random_bytes(8));
    $payload = json_encode(['type' => 'disconnect', 'at' => time()], JSON_UNESCAPED_UNICODE);
    db()->prepare('UPDATE remote_devices SET pending = ?, pending_id = ?, room = "", state = "idle",
                     session_claim = "" WHERE id = ?')
        ->execute([$payload, $commandId, $device['id']]);
    respond_ok(['command' => $commandId]);
}

/** Controller: renames a computer and/or turns its remote management on or off. */
function op_remote_set(array $input): void
{
    $owner = discord_identity($input);
    $device = require_owned_device($input, $owner);

    if (array_key_exists('enabled', $input)) {
        $enabled = !empty($input['enabled']) ? 1 : 0;
        db()->prepare('UPDATE remote_devices SET enabled = ? WHERE id = ?')->execute([$enabled, $device['id']]);
        if ($enabled === 0) {
            db()->prepare('UPDATE remote_devices SET pending = "", pending_id = "", room = "", state = "idle",
                             session_claim = "" WHERE id = ?')
                ->execute([$device['id']]);
        }
    }

    $name = text_field($input, 'name', MAX_DEVICE_NAME);
    if ($name !== '') {
        db()->prepare('UPDATE remote_devices SET name = ? WHERE id = ?')->execute([$name, $device['id']]);
    }

    respond_ok();
}

/** Controller: removes a paired computer. The installation notices on its next heartbeat. */
function op_remote_forget(array $input): void
{
    $owner = discord_identity($input);
    $device = require_owned_device($input, $owner);
    db()->prepare('DELETE FROM remote_devices WHERE id = ?')->execute([$device['id']]);
    respond_ok();
}

/** Installation: removes its own pairing, so the owner sees it gone as well. */
function op_remote_unpair(array $input): void
{
    $device = require_device($input);
    db()->prepare('DELETE FROM remote_devices WHERE id = ?')->execute([$device['id']]);
    respond_ok();
}
