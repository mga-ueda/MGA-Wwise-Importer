# MGA Wwise IMImporter

Nuendo の tracklist XML と Wave を読み、波形プレビューと分割 WAV の書き出しを行う Windows 向けツール（開発中）。Wwise への取り込みは未実装です。

## 使い方

`.wav` または同名ペアの `.xml` をドロップします（波形には `.wav` が必須）。同名 `.xml` があれば小節・テンポ・拍子・マーカー・リージョンを重ねます。起動時は `AutoLoadOnStartup=1` かつ `AutoLoadWavePath` があるとき自動読み込みします。マルチチャンネル（クアッド／5ch など Extensible 形式）の WAV にも対応し、プレビュー再生はステレオへダウンミックスされます。

出力パート（除外以外の連続リージョン）がある場合、ステータスバー上の［エクスポート］が有効になります（確認ダイアログは出さず、ログに案内を出します）。

| 操作 | 内容 |
|------|------|
| G | 小節番号を入力してジャンプ（Enter で確定、Esc でキャンセル） |
| Space | 再生／一時停止。`-L` 区間内（または再生で突入）では末尾で先頭へ無限ループ（サンプル単位で継ぎ目なし）。直後が `-E` なら、ループ頭へ戻る瞬間に Exit をワンショット二重再生（赤のシークバー／軌跡で可視化・Wwise 相当）。別位置へシークするとループ／Exit とも直ちに解除（行き先が別の `-L` ならその区間に付け替え）。シアンのシークバーが追従し、軌跡として残光を残す（停止時は消す）。ズーム中は画面外へ出たらページめくりで表示追従 |
| ↑ / ↓ / ホイール | 時間軸の拡大／縮小（既定＝全体表示より縮小しない） |
| Ctrl+↑ / Ctrl+↓ | 時間軸を最大倍率／既定（全体表示）に |
| Shift+↑ / Shift+↓ / Shift+ホイール | 縦方向（振幅）の拡大／縮小（既定より縮小しない） |
| Ctrl+Shift+↑ / Ctrl+Shift+↓ | 縦方向を最大倍率／既定に |
| Home / End | 再生位置を前／次の小節線へ（Home は押し続けている間は再生を止め、離すと再開） |
| Ctrl+← / Ctrl+→ | 再生位置を前／次のリージョン分割点へ（← は押し続けている間は再生を止め、離すと再開） |
| Ctrl+Home / Ctrl+End | 再生位置と表示窓を波形冒頭／末尾へ（Home 側は押し続けている間は再生を止め、離すと再開） |
| 波形上のマウス | 白い縦線でマウス位置を表示 |
| 波形クリック／ドラッグ | 再生位置を移動 |
| 最大化 | ウィンドウの最大化が可能 |
| ドロップ | `.wav`（＋同名 `.xml`）を読み込み、プレビュー更新。出力パートがあれば［エクスポート］を有効化 |
| 最前面 | ステータスバー上のチェック。ウィンドウを最前面に固定（`[Developer] TopMost` と連動。既定オフ） |
| クリア | 読み込み波形・ログを破棄 |
| エクスポート | ステータスバー上の青ボタン。分割 WAV 書き出し（続けて Wwise インポート可）。不可時は無効 |
| Ctrl+Shift+C | 色の開発者パネル（`[Colors]` INI） |

### プレビュー表示

上部にラベル行（小節番号／テンポ／拍子／マーカー）、左に行名（Measure／Tempo／Signature／Marker）と波形ファイル名、下部に波形です。

- **情報レーン** … 各ラベル行と同色の背景に行名。その下に読み込み中の波形ファイル名
- **小節番号** … 波形先頭基準の相対番号（1 始まり）。アウフタクト時は先頭半端小節が 1
- **テンポ・拍子** … 内部では各位置の値を保持し、表示は値が変わった位置だけ
- **マーカー** … Nuendo 単発マーカーを行表示
- **リージョン着色** … テンポ変化・拍子変化・サイクル In/Out などで分割。通常グレー／`-L`／`-A`／`-E`／`-R` は `[Colors]` の直値で塗る（`-R` のみ波形の上に重ねる）。`-L` 連続区間はプレビュー再生時に無限ループ。直後が `-E` ならループ頭へ戻る瞬間に Exit を並行ワンショット二重再生（シークで即停止）
- **リージョン境界** … 除外で区切られた連続リージョン固まりの頭／末尾に白い縦線＋下部の半四角（開始＝右半分、終了＝左半分）
- **Entry / Exit Cue** … 固まりの頭が Entry（ライム・上部半三角・開始形。先頭 `-A` ならその直後）、末尾が Exit（赤・上部半三角・終了形。末尾 `-E` ならその直前）。白より手前に描画
- **出力ラベル** … 連続する除外以外の範囲を `元名_1.wav` … として波形上に表示
- **TimeReference** … iXML（`BWF_TIME_REFERENCE_*`）のみ。無い／0 のときは波形先頭＝PPQ 0 扱い
- **読み込み演出** … ラベル → 波形ワイプ → 小節 → マーカー → 出力パート名の順で重ね表示

波形範囲外のマーカー／サイクルは描画せず、ログの「波形範囲外（無視）」に出します。

### 分割書き出し

- 出力パートがあるときだけ［エクスポート］が有効（確認ダイアログなし。準備状況はログに表示）
- 除外（`-R`）以外の連続リージョンを 1 ファイルにまとめ、ソースと同じディレクトリへ `元名_n.wav` で出力
- 各ファイルにはアプリ分割リージョンを cue / LIST adtl（`ltxt` + `rgn `）として埋め込む  
  （コメント例: `T120-4/4` / `T120-4/4-L` / `T120-4/4-E` / `T120-4/4-A`。接尾辞はスペース無しで連結）
- サイクル名の接尾辞: `-R`＝除外、`-L`＝ループ範囲、`-E`＝Wwise の Exit Cue 以降。  
  `-R` / `-L` / `-E`（および内部生成の `-A`）が重なる配置はエラー
- アプリ独自フォロー:
  - アウフタクト判定（冒頭半端小節、または `-R` Out 後の半端小節）のリージョンには `-A` を付与し、波形塗り・書き出し cue・Wwise Entry Cue に反映
  - `-L` 連続の直後リージョンに接尾辞が無いとき、`-E` を自動付与（明示の `-E` サイクルを省略可）。波形塗り・書き出し cue・Wwise Exit Cue に反映
- パート内の単発マーカーも cue（コメント付き）として埋め込む
- 書き出し中は該当パート枠が発光。結果はログの `=== Export ===` 以降に出力

### Wwise インポート（WAAPI）

書き出し完了後、Wwise（WAAPI 接続中）の**選択オブジェクトの下**へ Music 構造を自動生成できます（確認ダイアログあり）。

- 出力パートが 1 つ: Music Playlist Container（名前＝元ファイル名の拡張子抜き）
- 出力パートが 2 つ以上: Music Switch Container（元ファイル名）の下に Playlist × パート数（各エクスポートファイル名）。あわせて State Group（同名）を `[WwiseImport] StateGroupParentPath`（既定 `\States\Default Work Unit`）に作成し、各 State（エクスポート名）を同名 Playlist に割当。既存 State Group があるときは上書き／中断を確認。Switch の any→any トランジションは Exit Source at=Immediate、Source Fade-out ON（Time / Offset / Curve は WAAPI 非対応のため手設定）
- リージョン 1 つ = Music Segment 1 つ（名前は `_a` `_b` … の連番）。ただし:
  - `-A`（アウフタクト）は次のリージョンと同一セグメントにし、Entry Cue より前として扱う
  - `-E` は直前のリージョンと同一セグメントにし、Exit Cue より後として扱う（`-L` 直後への自動付与分も含む）
  - `-L` の付いたセグメントはプレイリスト上で無限ループ
- 各セグメントにはリージョンのテンポ・拍子を設定（Override）
- 単発マーカーは Custom Cue として付与
- トラックはストリーミング有効。Prefetch Length は全トラックに `[WwiseImport] PrefetchLengthMs`（既定 500）。各 Playlist の先頭セグメントは Zero latency オン＋Look-ahead 0、2 番目以降は Look-ahead を `[WwiseImport] LookAheadMs`（既定 500）に設定
- `[WwiseImport] WaveCopyDir` を設定すると、エクスポート WAV をそこへコピーしてからインポート（空なら書き出し場所から直接）。各 Music Segment 用にリージョン範囲の切り出し WAV も `_segments`（または `.mga_wwise_segments`）へ生成して取り込む（タイムライン先頭へ載せるため）

---

## ソース構成

| パス | 役割 |
|------|------|
| `Program.cs` | エントリポイント |
| `UI/` | フォーム・波形ビュー・ステータスバー・色定義・ウィンドウ／INI 設定 |
| `Processing/` | ドロップ処理（Wave／XML → プレビュー＋ログ） |
| `Nuendo/` | tracklist XML・テンポ／拍子マップ・小節境界 |
| `Wave/` | WAV／iXML・ピーク・再生・小節／リージョン／分割書き出し（cue 付与） |
| `Wwise/` | WAAPI 接続確認・Music 構造インポート（HTTP） |

名前空間はフォルダに合わせます（例: `MgaWwiseIMImporter.UI`、`MgaWwiseIMImporter.Wave`）。

UI はダーク基調（`UiColors`）。タイトルバーとログまわりもそれに寄せています。

---

## 開発メモ

- `test/` はローカル検証用でリポジトリには含めません（`.gitignore`）

### `MgaWwiseIMImporter.ini`

exe と同じフォルダに置きます（無ければ起動時に既定値で作成／不足キーを追記）。

| セクション | 内容 |
|------------|------|
| `[Developer]` | 自動読込パス・最前面 |
| `[Waapi]` | Wwise Authoring API 接続確認 |
| `[WwiseImport]` | Wwise への Music 構造インポート |
| `[Window]` | 位置・サイズ |
| `[Colors]` | UI 色（Ctrl+Shift+C パネルからも保存） |

#### `[Developer]`

| キー | 意味 | 既定 |
|------|------|------|
| `AutoLoadWavePath` | 自動読み込み対象の波形パス。相対パス可。オフ時もパスはそのまま残せる | リポジトリの `test\a.wav`（無ければ空） |
| `AutoLoadOnStartup` | 起動時に上記パスを自動読み込みする（`1`/`0`） | `1` |
| `TopMost` | ウィンドウを最前面に固定（`1`/`0`） | `0` |

```ini
[Developer]
AutoLoadWavePath=D:\GitHub\MGA-Wwise-IMImporter\test\a.wav
AutoLoadOnStartup=0
TopMost=0
```

#### `[Waapi]`

起動時にエディタログへ WAAPI 接続結果を出します（HTTP。既定ポートは 8090）。

| キー | 意味 | 既定 |
|------|------|------|
| `ProbeOnStartup` | 起動時に接続確認する（`1`/`0`） | `1` |
| `Url` | HTTP WAAPI の URL | `http://127.0.0.1:8090/waapi` |
| `TimeoutMs` | 接続・RPC のタイムアウト（ms） | `3000` |

```ini
[Waapi]
ProbeOnStartup=1
Url=http://127.0.0.1:8090/waapi
TimeoutMs=3000
```

#### `[WwiseImport]`

| キー | 意味 | 既定 |
|------|------|------|
| `LookAheadMs` | 2 番目以降のセグメントの Look-ahead time（ms、0〜10000） | `500` |
| `PrefetchLengthMs` | 全 Music Track の Prefetch Length（ms、0〜10000） | `500` |
| `WaveCopyDir` | エクスポート WAV のコピー先。空ならコピーせず書き出し場所から直接インポート | 空 |
| `StateGroupParentPath` | 複数パート時に作る State Group の親パス | `\States\Default Work Unit` |

```ini
[WwiseImport]
LookAheadMs=500
PrefetchLengthMs=500
WaveCopyDir=D:\wwise_test\ImportedWaves
StateGroupParentPath=\States\Default Work Unit
```

#### `[Colors]`

UI 色は Ctrl+Shift+C の開発者パネルからも保存します。波形リージョン塗り（`RegionWaveFill*`）は波形エリアに直値で適用されます。
