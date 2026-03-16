import { z } from "zod";

const AccountSchema = z.object({
  user: z.object({
    id: z.string(),
    username: z.string().optional(),
  }),
});

export async function fetchNakamaAccount(params: {
  nakamaHttp: string;
  serverKey: string;
  sessionToken: string;
}): Promise<{ userId: string; username?: string }> {
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