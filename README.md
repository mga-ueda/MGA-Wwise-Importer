# MGA Wwise IMImporter

Nuendo の tracklist XML と Wave を読み、波形プレビュー、分割 WAV の書き出し、WAAPI 経由の Wwise Music 構造インポートを行う Windows 向けツール（開発中）です。

## 使い方

`.wav` または同名ペアの `.xml` をドロップします（波形には `.wav` が必須）。同名 `.xml` があれば小節・テンポ・拍子・マーカー・リージョンを重ねます。起動時は `AutoLoadOnStartup=1` かつ `AutoLoadWavePath` があるとき自動読み込みします。マルチチャンネル（クアッド／5ch など Extensible 形式）の WAV にも対応し、プレビュー再生はステレオへダウンミックスされます。

出力パート（除外以外の連続リージョン）がある場合、画面下部の操作バー上の［エクスポート］が有効になります（確認ダイアログは出さず、ログに案内を出します）。

| 操作 | 内容 |
|------|------|
| G | 現在位置以前で最も近い小節番号を初期表示。1 以上の整数を Enter で確定してジャンプし、Esc でキャンセル。存在しない番号はログへ通知 |
| Space | 再生／一時停止。`-L` 区間内（または再生で突入）では末尾で先頭へ無限ループ（サンプル単位で継ぎ目なし）。直後が `-E` なら、ループ頭へ戻る瞬間に Exit をワンショット二重再生（赤のシークバー／軌跡で可視化・Wwise 相当）。別位置へシークするとループ／Exit とも直ちに解除（行き先が別の `-L` ならその区間に付け替え）。シアンのシークバーが追従し、軌跡として残光を残す（停止時は消す）。ズーム中は画面外へ出たらページめくりで表示追従 |
| ↑ / ↓ / ホイール | 時間軸の拡大／縮小（既定＝全体表示より縮小しない） |
| Ctrl+↑ / Ctrl+↓ | 時間軸を最大倍率／既定（全体表示）に |
| Shift+↑ / Shift+↓ / Shift+ホイール | 縦方向（振幅）の拡大／縮小（既定より縮小しない） |
| Ctrl+Shift+↑ / Ctrl+Shift+↓ | 縦方向を最大倍率／既定に |
| PageUp / PageDown | 再生位置を現在の表示幅 1 画面分だけ前／次へジャンプ（時間量ではなく現在の画面幅が単位。PageUp は押し続けている間は再生を止め、離すと再開） |
| Home / End | 再生位置を前／次の小節線へ（Home は押し続けている間は再生を止め、離すと再開） |
| Ctrl+← / Ctrl+→ | 再生位置を前／次のリージョン分割点へ（← は押し続けている間は再生を止め、離すと再開） |
| Ctrl+Home / Ctrl+End | 再生位置と表示窓を波形冒頭／末尾へ（Home 側は押し続けている間は再生を止め、離すと再開） |
| 波形上のマウス | `MouseGuide`（既定は半透明の白）の縦線でマウス位置を表示 |
| 拍グリッド | 表示範囲が8小節未満まで拡大されたとき、小節線より薄い拍線を表示。Measure／Music Segment Name／Music Playlist Nameレーンには描画しない |
| 波形クリック／ドラッグ | 再生位置を移動 |
| タイムラインをダブルクリック | 表示範囲と交差する Music Playlist がちょうど 1 つなら全体表示へ戻す。複数ある場合は、マウス直下の Playlist 範囲を表示幅の 90% にして中央表示。直下に Playlist がなければズームしないが、通常クリック相当のシークは発生 |
| 波形ファイル名 | 左側情報欄の幅をファイル名が一行で収まるよう自動調整し、名前の右側に再生出力の縦型レベルメーターを表示。最小／最大色のグラデーションは色設定から変更可能 |
| Fade In | Playlist左側のラジオボタンから遷移先のフェードイン時間を `None / 1.0 / 3.0 / 6.0 / 9.0 Sec.` で選択（既定はNone）。再生中のPlaylist遷移だけに適用し、停止中からの開始には適用しない。遷移先が `-A` なら先行再生開始からフェードインし、Entry Cue後も同じ包絡線を継続。変更時点ですでに待機中の遷移には影響せず、次の遷移予約から適用 |
| Fade Out | Playlist左側のラジオボタンから遷移元のフェードアウト時間を `0.5 / 1.0 / 3.0 / 6.0 / 9.0 Sec.` で選択。変更時点ですでに待機中の遷移には影響せず、次の遷移予約から適用 |
| Exit Source At | Fade Out下のラジオボタンから遷移元の退出位置を選択。`Immediate`＝即時、`Next Bar`＝次の小節、`Next Beat`＝次の拍、`Next Cue`＝現在Playlist内の次の単発マーカー、`Exit Cue`＝Playlist終端（末尾が `-E` ならその開始）。既定は `Next Bar`。変更は次の遷移予約から適用 |
| Dest. Sync To | Exit Source At下のラジオボタンから遷移先の開始位置を選択。`Entry Cue`＝従来どおりEntry Cueへ同期（既定）、`Same Time`＝実際の退出時点における現在Playlist先頭からの経過時間を遷移先へ引き継ぎ、レイヤーミュージックの縦遷移を模擬。遷移元の `-E` はFade Outが続く範囲で維持し、遷移先もループエンド到達時に通常どおり自身の `-E` を再生する。引継ぎ位置が遷移先の長さ以上なら予約しない |
| Playlist ボタン | ログ右側のフラットボタン一覧から遷移先を選択。一覧幅はファイル名が改行されない必要幅へ自動可変（表示領域を超える場合だけ省略表示）。ボタンは常にEnabledを維持し、非再生状態は暗い文字色だけで表現する。押下時も文字位置は動かさない。停止／一時停止中は対象の頭から即再生し、再生中は `Exit Source At` の位置で遷移。Dest. Sync ToがEntry Cueで遷移先冒頭が `-A` の場合は、その長さだけ先行して重ねる。Next Bar／Next Beatでは境界に `-A` 終端を揃え、Next Cue／Exit Cueまでの時間が足りない場合は `-A` を即開始して退出だけを指定Cueに揃える。Immediateでは旧曲フェードと `-A` を同時に即開始する。Same Timeでは `-A` 先行再生を使わず、引き継いだ相対位置から直接開始する。先行再生中の位置と残像は緑色で複製表示する。予約中も現在再生中のボタン色は遷移完了まで維持する。旧曲（再生中の `-E` を含む）は同期位置まで維持してから選択中のFade Outでフェードアウトし、遷移先は選択中のFade Inで立ち上げて新旧曲を重ね、Wwiseの曲推移を模擬。フェードアウト中の位置と残像は白で表示し、`-E` 到達時は白を消して赤いExit表示へ切り替える。停止中は薄いグレー、波形または一覧でマウス直下の項目は白文字。一覧ホバー時は対応する波形範囲を1px枠＋同色10%の半透明塗りで表示。再生中は自動＝シアン背景、手動＝黄色背景＋白文字。遷移待機中は予約先をテンポ／拍子に同期してフェード点滅し、完了時はボタン枠を次の拍頭まで滑らかに消灯 |
| Esc | 通常画面では終了確認を表示。小節ジャンプではキャンセル、色パネルでは閉じる |
| 最大化 | ウィンドウの最大化が可能 |
| ドロップ | `.wav`（＋同名 `.xml`）を読み込み、プレビュー更新。出力パートがあれば［エクスポート］を有効化 |
| ログ右下ボタン | ［クリア］でログ表示だけを消去、［コピー］で全ログをクリップボードへコピー、［ダウンロード］で UTF-8 の `.log`／`.txt` ファイルへ保存 |
| 詳細ログ | 操作バーのチェック。Playlist推移・再生エンジン診断を `[PlaybackDebug]` JSON Lines形式でログへ出力（既定オン） |
| 最前面 | WAAPI ステータスバー直上の操作バーにあるチェック。ウィンドウを最前面に固定（`[Developer] TopMost` と連動。既定オフ） |
| クリア | 読み込み波形・ログを破棄 |
| エクスポート | WAAPI ステータスバー直上の操作バーにある青ボタン。分割 WAV 書き出し（続けて Wwise インポート可）。不可時は無効 |
| Ctrl+Shift+C | 色の開発者パネル（`[Colors]` INI） |

### プレビュー表示

上部にラベル行（小節番号／テンポ／拍子／マーカー）、左に行名（Measure／Tempo／Signature／Marker／Music Segment Name／Music Playlist Name）と波形ファイル名、中央に波形、下に各名前レーンです。

- **情報レーン** … 各ラベル行と同色の背景に行名。波形左に読み込み中のファイル名。下部レーン左は Music Segment Name／Music Playlist Name
- **小節番号** … 波形先頭基準の相対番号（1 始まり）。アウフタクト時は先頭半端小節が 1
- **テンポ・拍子** … 内部では各位置の値を保持し、表示は値が変わった位置だけ
- **マーカー** … Nuendo 単発マーカーを行表示
- **リージョン着色** … テンポ変化・拍子変化・サイクル In/Out などで分割。通常グレー／`-L`／`-A`／`-E`／`-R` は `[Colors]` の直値で塗る（`-R` は波形と Music Segment／Playlist レーンの上に重ねる）。`-L` 連続区間はプレビュー再生時に無限ループ。直後が `-E` ならループ頭へ戻る瞬間に Exit を並行ワンショット二重再生（シークで即停止）
- **リージョン境界** … 除外で区切られた連続リージョン固まりの頭／末尾、および Wwise Music Segment の分かれ目に `RegionBoundaryMarker`（既定は明るいグレー）の縦線＋下部の半四角（開始＝右半分、終了＝左半分）
- **Entry / Exit Cue** … 固まりの頭が Entry（`EntryCueMarker`、既定はライム。上部半三角・開始形。先頭 `-A` ならその直後）、末尾が Exit（`ExitCueMarker`、既定は赤。上部半三角・終了形。末尾 `-E` ならその直前）。リージョン境界より手前に描画
- **Music Segment 名** … 波形下の Music Segment Name レーン（高さは Measure 行と同じ）に、リージョン束ね単位のセグメント名（`…_a` / `…_b`。`-A`／`-E` の束ね含む）を通常ウェイトで表示。Playlist より細かい。時間的に連続するセグメント同士の境だけ、波形背景色の 3 px 縦線を描く（`-R` などの隙間には描かない）
- **Music Playlist 名** … その下の Music Playlist Name レーン（同高さ）に、エクスポートファイル名（拡張子除く）＋` (.wav)`（例: `元名_1 (.wav)`）を太字で表示。複数パート時は Wwise Playlist 名と一致するが、単一パート時に作る Wwise Playlist Container 名は元ファイル名
- **名前レーンの文字** … Segment／Playlist とも各時間範囲の内側に収まるまで縮小。極端に狭い場合は最小 0.5 px とし、それでも収まらなければ横方向を縮めるため、拡大すると全文を確認できる
- **Playlist 遷移シミュレーター** … ログ右側に出力パート由来の Playlist 名を縦表示。件数が多い場合はスクロールし、長い名前は省略表示してホバーで全文を表示する。再生中の選択は Exit Source At（Immediate／Next Bar／Next Beat／Next Cue／Exit Cue）へクオンタイズし、Dest. Sync ToのEntry CueまたはSame Timeで遷移先位置を決める。Same Timeはボタン押下時ではなく実際の退出時点の相対時間を使う。`-L` ループ中でも到達可能な境界では Playlist 遷移を優先し、Next Cueは現在周回のループ終端までに次の単発マーカーがなければ予約しない。遷移待ち中の再選択は最後に選んだ Playlist を採用する。波形側でシークすると自動状態と予約を解除し、現在位置の Playlist を黄色表示へ切り替える
- **TimeReference** … iXML（`BWF_TIME_REFERENCE_*`）のみ。無い／0 のときは波形先頭＝PPQ 0 扱い
- **読み込み演出** … ラベル → 波形ワイプ → 小節 → マーカー → Playlist／Segment 名の順で重ね表示

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
| `DetailedPlaybackLog` | Playlist推移・transport操作・ループ／Exit・音声プロバイダー内部遷移をJSON Linesで記録（`1`/`0`） | `1` |

```ini
[Developer]
AutoLoadWavePath=D:\GitHub\MGA-Wwise-IMImporter\test\a.wav
AutoLoadOnStartup=0
TopMost=0
DetailedPlaybackLog=1
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

UI 色は Ctrl+Shift+C のモードレスな開発者パネルから編集できます。スウォッチまたは RGB 値を変更すると即時反映・INI 保存され、Enter またはフォーカス移動で確定、Esc で閉じます。値は `#RRGGBB` 形式で保存し、アルファ値は各色のコード既定値を使用します。Playlist の背景・通常／ホバー／再生中文字・自動／手動再生背景は `Playlist*` キーで個別編集でき、既存 INI にないキーは起動時に自動追記します。波形リージョン塗り（`RegionWaveFill*`）は波形エリアに直値で適用され、`RegionWaveFillExcluded` は Music Segment／Playlist レーンにも重ねて表示されます。
