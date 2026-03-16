"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.fetchNakamaAccount = fetchNakamaAccount;
const zod_1 = require("zod");
const AccountSchema = zod_1.z.object({
    user: zod_1.z.object({
        id: zod_1.z.string(),
        username: zod_1.z.string().optional(),
    }),
});
async function fetchNakamaAccount(params) {
    const url = `${params.nakamaHttp.replace(/\/$/, "")}/v2/account`;
    const res = await fetch(url, {
        method: "GET",
        headers: {
            Authorization: `Bearer ${params.sessionToken}`,
            Accept: "application/json",
        },
    });
    if (!res.ok) {
        const text = await res.text().catch(() => "");
        throw new Error(`Nakama /v2/account failed: ${res.status} ${text}`);
    }
    const json = await res.json();
    const parsed = AccountSchema.parse(json);
    return { userId: parsed.user.id, username: parsed.user.username };
}
