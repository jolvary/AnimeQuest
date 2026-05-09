function InitModule(ctx, logger, nk, initializer) {
  initializer.registerRpc("client_logs", rpcClientLogs);
  logger.info("[Dozzle][Unity] client log RPC registered");
}

function rpcClientLogs(ctx, logger, nk, payload) {
  var body = {};
  try {
    body = payload ? JSON.parse(payload) : {};
  } catch (error) {
    logger.warn("[Dozzle][Unity] invalid client log payload");
    return JSON.stringify({ ok: false });
  }

  var level = compact(body.level, 20).toLowerCase() || "action";
  var action = compact(body.action, 120) || "Unity event";
  var details = compact(body.details, 1000);
  var message = compact(body.message, 1000);
  var clientTimestamp = compact(body.timestamp, 80);
  var platform = compact(body.platform, 80);
  var unityVersion = compact(body.unityVersion, 80);
  var userId = compact(ctx.userId, 80);
  var username = compact(ctx.username, 120);

  var text = "[Dozzle][Unity] " + action;
  var parts = [];
  if (details) parts.push(details);
  if (message) parts.push("message=" + message);
  if (clientTimestamp) parts.push("clientTimestamp=" + clientTimestamp);
  if (platform) parts.push("platform=" + platform);
  if (unityVersion) parts.push("unityVersion=" + unityVersion);
  if (userId) parts.push("userId=" + userId);
  if (username) parts.push("username=" + username);
  if (parts.length > 0) {
    text += " | " + parts.join(";");
  }

  if (level === "error") {
    logger.error(text);
  } else if (level === "warning" || level === "warn") {
    logger.warn(text);
  } else {
    logger.info(text);
  }

  return JSON.stringify({ ok: true });
}

function compact(value, maxLength) {
  if (typeof value !== "string") return "";

  var trimmed = value.trim();
  if (!trimmed) return "";

  if (trimmed.length > maxLength) {
    return trimmed.substring(0, maxLength) + "...";
  }

  return trimmed;
}
