#!/usr/bin/env python3
"""Discord の Webhook へ embed を投稿する。

dev-report.py から import して使うほか、ワークフローの通知ステップから
コマンドとしても呼べる (標準ライブラリだけで動く)。

    python3 .github/scripts/discord_webhook.py --title "..." --description-file report.md \
        --color red --url "$RUN_URL"

Webhook の URL は環境変数 DISCORD_WEBHOOK_URL から読む。未設定なら警告だけ出して
何もせず終了コード 0 で抜ける (通知が無いことで CI を落とさない。ci.yml の notify と同じ方針)。

Discord の上限 (https://discord.com/developers/docs/resources/message#embed-object-embed-limits):
  title 256 / description 4096 / field.value 1024 / footer.text 2048 文字、
  1 メッセージの embed は 10 個まで、かつ embed の文字数の合計は 6000 まで。
長い本文はこのモジュールが複数の embed・複数のメッセージに分ける。
"""

import argparse
import json
import os
import sys
import time
import urllib.error
import urllib.request

TITLE_LIMIT = 256
DESCRIPTION_LIMIT = 4096
FOOTER_LIMIT = 2048
EMBEDS_PER_MESSAGE = 10
MESSAGE_TOTAL_LIMIT = 6000
# 分割の単位。上限ちょうどまで詰めると 1 メッセージに 1 embed しか入らないので控えめにする。
CHUNK_CHARS = 3800

COLORS = {
    "red": 14687012,
    "green": 3055683,
    "blue": 5793266,
    "yellow": 16705372,
    "gray": 9807270,
}

USER_AGENT = "vs-extensions-ci (https://github.com/noxitro/vs-extensions, 1.0)"


def split_text(text, limit=CHUNK_CHARS):
    """行の切れ目で limit 以下に分ける。コードブロックの途中で切れたら閉じて次で開き直す。"""
    chunks = []
    current = ""
    for line in text.split("\n"):
        # 1 行だけで上限を超えるものは強制的に切る
        while len(line) > limit:
            head, line = line[:limit], line[limit:]
            if current:
                chunks.append(current)
                current = ""
            chunks.append(head)
        candidate = line if not current else current + "\n" + line
        if len(candidate) > limit:
            chunks.append(current)
            current = line
        else:
            current = candidate
    if current or not chunks:
        chunks.append(current)

    # ``` の数が奇数の chunk はコードブロックの途中で切れている
    fixed = []
    carry = False
    for chunk in chunks:
        if carry:
            chunk = "```\n" + chunk
        opened = chunk.count("```") % 2 == 1
        if opened:
            chunk = chunk + "\n```"
        carry = opened
        fixed.append(chunk)
    return fixed


def text_embeds(title, text, color, url=None, footer=None):
    """本文を分割して embed の列にする。2 つ目以降のタイトルには (続き) を付ける。
    url は先頭の embed にだけ付ける。同じ url の embed が 1 メッセージに並ぶと、Discord は
    画像ギャラリーとしてまとめてしまい、2 つ目以降を表示しない。
    embed 1 つの合計 (タイトル + 本文 + フッター) も 6000 文字以下に収める。"""
    embeds = []
    title = clip(title, TITLE_LIMIT - len(" (続き)"))
    footer = clip(footer, FOOTER_LIMIT) if footer else None
    # 続きのタイトルとコードブロックの閉じ直し (8 文字) の分も空けておく
    room = MESSAGE_TOTAL_LIMIT - len(title) - len(" (続き)") - len(footer or "") - 8
    for i, chunk in enumerate(split_text(text or "(なし)", min(CHUNK_CHARS, room))):
        embed = {
            "title": clip(title if i == 0 else f"{title} (続き)", TITLE_LIMIT),
            "description": chunk,
            "color": color,
        }
        if url and i == 0:
            embed["url"] = url
        embeds.append(embed)
    if footer:
        embeds[-1]["footer"] = {"text": clip(footer, FOOTER_LIMIT)}
    return embeds


def clip(s, limit):
    return s if len(s) <= limit else s[: limit - 1] + "…"


def embed_size(embed):
    size = len(embed.get("title", "")) + len(embed.get("description", ""))
    size += len(embed.get("footer", {}).get("text", ""))
    size += len(embed.get("author", {}).get("name", ""))
    for f in embed.get("fields", []):
        size += len(f.get("name", "")) + len(f.get("value", ""))
    return size


def pack_messages(embeds):
    """embed の列を、1 メッセージの上限 (10 個 / 合計 6000 文字) に収まるよう束ねる。"""
    messages = []
    current, total = [], 0
    for embed in embeds:
        size = embed_size(embed)
        if current and (len(current) >= EMBEDS_PER_MESSAGE or total + size > MESSAGE_TOTAL_LIMIT):
            messages.append(current)
            current, total = [], 0
        current.append(embed)
        total += size
    if current:
        messages.append(current)
    return messages


def post(webhook, payload, retries=5):
    """1 メッセージ分を投稿する。429 は retry_after に従い、5xx は指数バックオフで再送する。
    Webhook の URL にはトークンが含まれるので、どの失敗でも例外の文面に URL を載せない。"""
    webhook = webhook.strip()
    url = webhook + ("&" if "?" in webhook else "?") + "wait=true"
    body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    for attempt in range(retries):
        try:
            # Request の生成も try の中に置く。URL が不正だと、URL を含む ValueError になる
            req = urllib.request.Request(url, data=body, method="POST", headers={
                "Content-Type": "application/json; charset=utf-8",
                "User-Agent": USER_AGENT,
            })
            with urllib.request.urlopen(req, timeout=30) as resp:
                resp.read()
                return
        except urllib.error.HTTPError as e:
            try:
                detail = e.read().decode("utf-8", "replace")
            except Exception:  # noqa: BLE001  エラー本文を読む途中で切れても、再試行の判断は続ける
                detail = ""
            if e.code == 429 and attempt + 1 < retries:
                try:
                    wait = float(json.loads(detail).get("retry_after") or 2)
                except (ValueError, AttributeError, TypeError):
                    wait = 2.0
                time.sleep(min(wait, 60) + 0.5)
                continue
            if e.code >= 500 and attempt + 1 < retries:
                time.sleep(2 ** attempt)
                continue
            # URL にはトークンが含まれるので、エラーには出さない
            raise RuntimeError(f"Discord への投稿に失敗した: HTTP {e.code} {detail[:500]}") from None
        except urllib.error.URLError as e:
            # reason は文字列 ("unknown url type: htps" など、設定の誤り) か OSError (接続の失敗)。
            # どちらも URL 自体は含まない。設定の誤りは再試行しても直らない。
            if isinstance(e.reason, OSError):
                reason = f"{type(e.reason).__name__}: {e.reason.strerror or ''}".rstrip(": ")
                if attempt + 1 < retries:
                    time.sleep(2 ** attempt)
                    continue
            else:
                reason = str(e.reason)
            raise RuntimeError(f"Discord への接続に失敗した: {reason}") from None
        except Exception as e:  # noqa: BLE001  URL を含みうる文面を出さないため、型名だけにする
            # 応答を読む途中で切れた場合などは、投稿済みかもしれないので再送しない
            raise RuntimeError(f"Discord への投稿に失敗した: {type(e).__name__}") from None


def send(webhook, embeds, username=None, dry_run=False):
    """embed の列を必要な数のメッセージに分けて投稿する。送ったメッセージ数を返す。"""
    messages = pack_messages(embeds)
    for embeds_in_message in messages:
        payload = {"embeds": embeds_in_message, "allowed_mentions": {"parse": []}}
        if username:
            payload["username"] = username
        if dry_run:
            print(json.dumps(payload, ensure_ascii=False, indent=2))
            continue
        post(webhook, payload)
    return len(messages)


def main():
    # Windows のランナーは標準出力が cp1252 なので、日本語で落ちないようにする
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="replace")
    ap = argparse.ArgumentParser(description="Discord の Webhook へ embed を投稿する")
    ap.add_argument("--title", required=True)
    group = ap.add_mutually_exclusive_group(required=True)
    group.add_argument("--description")
    group.add_argument("--description-file")
    ap.add_argument("--color", default="gray", help="red / green / blue / yellow / gray か 10 進数")
    ap.add_argument("--url", help="タイトルのリンク先")
    ap.add_argument("--footer")
    ap.add_argument("--username", default="GitHub Actions")
    ap.add_argument("--dry-run", action="store_true", help="投稿せずに payload を表示する")
    args = ap.parse_args()

    webhook = os.environ.get("DISCORD_WEBHOOK_URL", "").strip()
    if not webhook and not args.dry_run:
        print("::warning::DISCORD_WEBHOOK_URL が未設定なので通知をスキップする")
        return 0

    if args.description_file:
        with open(args.description_file, encoding="utf-8") as f:
            text = f.read().strip()
    else:
        text = args.description
    color = COLORS.get(args.color)
    if color is None:
        color = int(args.color)

    embeds = text_embeds(args.title, text, color, url=args.url, footer=args.footer)
    count = send(webhook, embeds, username=args.username, dry_run=args.dry_run)
    print(f"Discord に {count} 件のメッセージを送った" + (" (dry-run)" if args.dry_run else ""))
    return 0


if __name__ == "__main__":
    sys.exit(main())
