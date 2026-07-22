# MGA Wwise IMImporter

Nuendo／Cubase の tracklist XML と Wave を読み、波形プレビュー、分割 WAV の書き出し、WAAPI 経由の Wwise Music 構造インポートを行う Windows 向けツール（開発中）です。

## 使い方

`.wav` または同名ペアの `.xml` をドロップします（波形には `.wav` が必須）。同名 `.xml` があれば小節・テンポ・拍子・マーカー・リージョンを重ねます。Wave／XML をドロップするとアプリが前面アクティブになります。`Keep Last Session` がオンなら、起動時およびそのプロジェクトへ戻ったときに最後の作業セッションを自動復元します。マルチチャンネル（クアッド／5ch など Extensible 形式）の WAV にも対応し、プレビュー再生はステレオへダウンミックスされます。

出力パートがあり、かつ書き出し条件（Wwise 接続・作成先選択・プロジェクト書き出し先が接続中プロジェクトの Originals 配下）を満たすとき、画面下部の操作バー上の［EXPORT］が有効になります（条件未達の理由はログに出します）。

画面は上から **プロジェクトバー → 波形 → TRANSPORT → ログ／遷移パネル → 操作バー → WAAPI ステータスバー** の順です。以下も同じ順で説明します。

### プロジェクトバー（上端）

左から: プロジェクト名コンボ → 書き出しパス → フォルダ／削除 → `Keep Last Session` → `Always on Top` → JP／EN → スペクトラム。

| 操作 | 内容 |
|------|------|
| プロジェクト名 | 選択と編集。末尾の `+ New Project` で新規作成。変更時に `[Project.*]` へオートセーブ（名前変更はコンボからフォーカスが外れたとき） |
| + New Project | 波形・セッション・ログをすべて卸し、アプリ既定設定の `New Project`（既存なら `New Project 2` …）を即時 INI 保存して切り替える。ログには作成メッセージだけを残す |
| 書き出しパス／フォルダ | 分割 WAV の書き出し先（接続中 Wwise プロジェクトの Originals 配下）。フォルダボタンで選択 |
| 削除 | 選択中プロジェクトを削除（DEL） |
| Keep Last Session | 既定オン。起動時およびこのプロジェクトへ戻ったときに、最後の作業セッションを復元（`[Project.*] KeepLastSession` / `LastWavePath`）。波形に加え、同プロジェクトのサイドカー JSON のグループ／無効化／追加マーカー／Fade In・Out（通常／Group）／Exit Source At（パート別）を復元 |
| Always on Top | 既定オフ。ウィンドウを最前面に固定（`[App] AlwaysOnTop`） |
| JP／EN | 表示言語切替。ラベル・ボタン・ツールチップ・ダイアログ・ログ・アクセシビリティ名を含むすべての表示テキスト（`[App] UiLanguage`。既定 `ja`） |
| スペクトラム | 再生出力の簡易スペクトラム表示 |

### 波形プレビュー

| 操作 | 内容 |
|------|------|
| ドロップ | `.wav`（＋同名 `.xml`）を読み込み、プレビュー更新。書き出し条件を満たせば［EXPORT］を有効化。ドロップ時にアプリを前面アクティブにする |
| 波形クリック／ドラッグ | 再生位置を移動 |
| ← / → | シークバーを波形上 1px 分だけ移動（キーリピート可） |
| 数字キー／テンキー 0〜9 | **いま表示している画面内**の 0%〜90% へジャンプ（文字入力フォーカス中は無効） |
| C / . | シーク位置はそのまま、表示だけ中央寄せ |
| 波形上のマウス | 半透明の縦線でマウス位置を表示 |
| タイムラインをダブルクリック | 表示範囲と交差する Music Playlist がちょうど 1 つなら全体表示へ戻す。複数ある場合は、マウス直下の Playlist 範囲を表示幅の 90% にして中央表示。直下に Playlist がなければズームしないが、通常クリック相当のシークは発生 |
| 波形ファイル名 | 左側情報欄の幅をファイル名が一行で収まるよう自動調整し、名前の右側に再生出力の縦型レベルメーターを表示 |
| 波形ファイル名をダブルクリック | 拡張子なしの基底名を編集。Enter またはフォーカス移動で確定、Esc でキャンセル。Playlist／Segment 表示、書き出し WAV 名、Wwise の Container／Playlist／Segment／Track 名に反映 |
| Shift＋Marker レーンを左ドラッグ | Music Playlist 範囲内へマーカーを追加（Playlist 範囲外および `-A`／`-E` 区間には追加しない）。スナップ単位は Marker Grid（`Bar`／`Beat`／`Timeline`）。追加分は Digits／Zero Pad／Prefix／Suffix／Separator／Reset Per Part に従いコメントを付け、現在の読み込み中だけ保持し、Next Cue・Wwise Custom Cue にも使用（WAV へは埋め込まない）。グループ上では最若番号 Playlist の相対位置へ記録し全メンバーへ共有 |
| Ctrl＋Marker レーンを左ドラッグ | この操作で追加したマーカーだけを消去。グループでは全メンバーの同じ共有マーカーを消去。XML 由来マーカーは変更しない |
| 拍グリッド（表示） | 表示範囲が 8 小節未満まで拡大されたとき、小節線より薄い拍線を表示。Measure／Music Segment Name／Music Playlist Name レーンには描画しない |
| 波形下部スクロールバー | 時間軸の表示位置を移動。全体表示中も細いダークトラックを常時表示 |
| 最大化 | ウィンドウの最大化が可能 |

#### Wave 単体モード（同名 XML なし・埋め込みマーカーのみ／無し）

ペア XML が無く、埋め込みがマーカーのみ（または無し）のとき、Wave 単体モードになります（smpl ループ／リージョン埋め込みは未実装）。マーカーはアプリ上だけで編集し、WAV には書き戻しません。

| 操作 | 内容 |
|------|------|
| マーカー無し | 冒頭を Entry Cue、末尾を Exit Cue として全体を 1 パート扱い |
| ▼ドラッグ | マーカーを移動。**Alt＋ドラッグ**で一つ前のマーカーも同量移動 |
| ← / → | シークバーを 1px 移動 |
| Alt＋← / → | シーク位置にちょうどマーカーがあるとき、マーカーとシークを 1px 移動（キーリピート可） |
| ▼／コメントをダブルクリック | コメント編集。埋め込み由来の改名はログに「旧→新」をオレンジで表示（`Loop`→`-L`、2 マーカー特例の `-L`/`-E` 実体化も含む） |
| Ctrl+Shift+R | シーク位置のマーカーをリネーム |
| Delete / Ctrl+Shift+Del | 選択中、またはシーク位置のマーカーを削除 |
| Insert | シーク位置にコメントなしマーカーを追加 |
| Ctrl+Z / Ctrl+Shift+Z / Ctrl+Y | Undo / Redo |
| Home / End | 表示幅の約 5% 前後へシーク |
| Ctrl＋← / Ctrl＋→ | 前後のマーカーへシーク |
| コメント | `-L` 無限ループ、`-R` リムーブ、`-E` Exit Cue 以降、`-A` Entry Cue 前。コメントに `Loop` を含む場合も `-L` 相当。マーカーがちょうど 2 つで未実体化なら間を `-L`（実体化で 1 つ目 `-L`／2 つ目 `-E`） |

### TRANSPORT

左から: 位置表示 → TRANSPORT（再生／G）→ NAVIGATION → TIME ZOOM → AMP ZOOM。

| 操作 | 内容 |
|------|------|
| Space | 再生／一時停止。`-L` 区間内（または再生で突入）では末尾で先頭へ無限ループ（サンプル単位で継ぎ目なし）。直後が `-E` なら、ループ頭へ戻る瞬間に Exit をワンショット二重再生（赤のシークバー／軌跡で可視化・Wwise 相当）。別位置へシークするとループ／Exit とも直ちに解除（行き先が別の `-L` ならその区間に付け替え）。シアンのシークバーが追従し、軌跡として残光を残す（停止時は消す）。ズーム中は画面外へ出たらページめくりで表示追従 |
| Alt+Enter / Alt＋再生クリック | 直前の再生開始位置から再生し直し |
| Ctrl+Space / Ctrl＋再生クリック | 現在位置の約 3 秒前から再生 |
| G | 現在位置以前で最も近い小節番号を初期表示。1 以上の整数を Enter で確定してジャンプし、Esc でキャンセル。存在しない番号はログへ通知 |
| ↑ / ↓ / ホイール | 時間軸の拡大／縮小（既定＝全体表示より縮小しない） |
| Ctrl+↑ / Ctrl+↓ | 時間軸を最大倍率／既定（全体表示）に。全体表示時、`-R` 除外範囲はフィット対象から外す |
| Shift+ホイール | 拡大中の時間軸を左右へスクロール |
| Shift+↑ / Shift+↓ / Ctrl+ホイール | 縦方向（振幅）の拡大／縮小（既定より縮小しない） |
| Ctrl+Shift+↑ / Ctrl+Shift+↓ | 縦方向を最大倍率／既定に |
| PageUp / PageDown | 再生位置を現在の表示幅 1 画面分だけ前／次へ（PageUp は押し続けている間は再生を止め、離すと再開） |
| Home / End | 再生位置を前／次の小節線へ（Wave 単体モードでは表示幅の約 5%。Home は押し続けている間は再生を止め、離すと再開） |
| Ctrl+← / Ctrl+→ | 再生位置を前／次の Music Playlist へ（Wave 単体モードでは前後マーカー。← は押し続けている間は再生を止め、離すと再開） |
| Ctrl+Home / Ctrl+End | 再生位置と表示窓を波形冒頭／末尾へ（Home 側は押し続けている間は再生を止め、離すと再開） |
| Esc | 通常画面では終了確認。小節ジャンプ（G）ではキャンセル |

### ログ／遷移パネル（中央）

左にログ、右に Fade／Exit／Music Playlist／More Options。

| 操作 | 内容 |
|------|------|
| ログ右下アイコン | クリア → コピー → 保存（ログ表示の消去／クリップボード／UTF-8 の `.log`／`.txt`。各機能名はツールチップ） |
| Fade In | 左のラジオから `None / 1.0 / 3.0 / 6.0 / 9.0 Sec.`（既定 None）。**いま再生しているソース**の振る舞い。Playlist を選んでから変更するとパート（グループ）単位で記憶。再生中の Playlist 遷移だけに適用。遷移先が `-A` なら先行再生開始からフェードイン。待機中の遷移には影響せず、次の予約から適用。Wwise の Destination Fade-in は操作しない（下記「フェードと Wwise トランジション」） |
| Fade Out | 同様の秒数候補（既定 None）。**いま再生しているソース**側。待機中の遷移には影響せず、次の予約から適用 |
| Exit Source At | Fade Out 右（既定 Immediate）。Playlist ごとに Fade In／Fade Out／Group Fade／Exit Source At を記憶。Fade In／Out 内は通常候補の下に `Group` 見出しでグループ用を区分。同一グループ ID で共通同期。同一グループ内遷移は Group Fade のみ（通常 Fade は無効）。グループ外からの遷移は通常の Fade In/Out。サイドカーへオートセーブ／復元。Wwise へは Exit Source At のみ渡し、Group Fade は未使用 |
| 遷移先同期（自動） | 同一グループ内は `Same Time`、それ以外は `Entry Cue`。`Same Time` は実際の退出時点の相対時間を引き継ぐ。遷移先の長さ以上なら予約しない |
| Music Playlist 一覧 | 遷移先を選択。一覧幅はファイル名が改行されない必要幅へ自動可変（超える場合だけ省略）。停止／一時停止中は対象の頭から即再生、再生中は `Exit Source At` で遷移。同一グループ内の `Same Time` では相対位置から開始、それ以外の `Entry Cue` で遷移先冒頭が `-A` ならその長さだけ先行重ね。Next Bar／Next Beat では境界に `-A` 終端を揃え、Next Cue／Exit Cue までの時間が足りない場合は `-A` を即開始して退出だけを指定 Cue に揃える。Immediate では旧曲フェードと `-A` を同時に即開始。先行再生は緑、フェードアウトは白、`-E` 到達後は赤。通常時は枠なし、再生中は背景塗り、遷移完了時は枠をフェード発光。遷移待機中は予約先の枠がテンポ／拍子に同期して点滅 |
| Shift＋Playlist 項目を左クリック／ドラッグ | グループ化（縦レイヤー）。既存グループも新しい ID で上書き可。最若番号 Playlist のマーカーへ同期。`Reset Per Part` 無効時の全体連番も基準 Playlist 側だけを対象 |
| Ctrl＋Playlist 項目を左クリック／ドラッグ | グループ解除。各 Playlist 固有の元マーカー情報へ戻る |
| Ctrl＋Shift＋Playlist 項目を左クリック／ドラッグ | 書き出し対象外／再有効化。一覧は `Excluded Region n` の赤文字、波形は約 25% 表示。再生・遷移・グループ化・マーカー編集・WAV 書き出し・Wwise インポートの対象外 |
| Compact Num. | Music Playlist 一覧下端（既定オフ）。無効項目を除いた書き出し WAV・Playlist・Segment 名の番号を 1 から詰める。オフでは元番号を維持し欠番を残す |
| More Options | 右パネル下部の折りたたみ（既定開き）。開閉は `[Project.*] MoreOptionsExpanded` に保存。開閉時はウィンドウ高さを調整し Music Playlist の高さは維持。Music Playlist（Compact Num. 含む）の下端は Fade In セクション下端に揃える |
| Stream | More Options 上段左。`Stream`（既定オン）→ `Prefetch Length` → `Look-ahead Time`（ms、0〜9999、既定 500）。`[Project.*] StreamEnabled`／`PrefetchLengthMs`／`LookAheadMs`。オフ時は Prefetch／Look-ahead は無効で、Wwise へもストリーミングなしで作成 |
| Loudness Normalize | More Options 上段中央。`Normalize`（既定オフ）／`Target` LKFS（既定 −24）／`Preserve Group Balance`（既定オン）。Wwise の非破壊 Loudness Normalize とは別の、このアプリ独自機能。EXPORT 時に分割 WAV へ破壊編集でゲインを焼き込む |
| Auto Volume | More Options 上段右。`Auto Volume`（既定オフ）／`Make-Up Gain`（既定）または `Voice Volume`。Normalize で変化した音量の逆を Music Playlist へ書き戻す。Normalize オフ時は無効 |
| Marker Grid | More Options 下段左。スナップ単位 `Bar`／`Beat`／`Timeline`（既定 `Bar`）。縦線の描画には影響しない。`Timeline` は表示中グリッドに合わせる（8 小節未満なら拍、それ以外は画面に出ている小節線） |
| Marker Comment | More Options 下段右。`Digits`／`Zero Pad`／`Reset Per Part`／`Prefix`／`Suffix`／`Separator`。選択中プロジェクトの `[Project.*]` へ自動保存 |

### 操作バー／WAAPI ステータスバー（下端）

操作バーは左にロゴ・著作権、右に `CLEAR` → `RELOAD` → `EXPORT`。その下が WAAPI ステータスバー。

| 操作 | 内容 |
|------|------|
| CLEAR | 波形・セッション・ログをクリアし、選択中プロジェクトの設定をアプリ既定へ戻して保存する。**書き出し先フォルダ・WAAPI Keep Target・`Always on Top`（アプリ設定）は変わらない**。プロジェクト自体は削除しない |
| RELOAD | 最後にドロップまたは自動読み込みした WAV／XML を元ファイルから再読み込みする。ログは消去。サイドカー JSON があればグループ／無効化／追加マーカー／Fade・Exit Source At を復元。再生は一時コピー経由のため、読み込み中も外部アプリは元 WAV を上書き可能 |
| EXPORT | プロジェクト書き出し先へ分割 WAV を書き出し、続けて Wwise へ登録（**Ctrl+Shift+E**）。Wwise 未接続／作成先未選択／書き出し先が未指定・不存在・Originals 外のときは無効 |
| Keep Target | ステータスバー末尾の鍵アイコン。表示は `Wwise v… - プロジェクト名 - 作成先パス`＋鍵＋`- Keep Target -`／`- Not Keep Target -`（省略なし）。クリックで作成先を**選択中プロジェクト**に固定／解除。固定中は黄色の施錠、未固定は白の開錠。未接続時は赤字のエラー表示（固定は外さない）。再接続できたら記憶パスを有効化。起動時／EXPORT 前は可能なら Wwise 上でも同パスを再選択（`[Project.*] KeepTarget`／`KeptTargetPath`。既定オフ） |

### プレビュー表示

上部にラベル行（小節番号／テンポ／拍子／マーカー）、左に行名（Measure／Tempo／Signature／Marker／Music Segment Name／Music Playlist Name）と波形ファイル名、中央に波形、下に各名前レーンです。

- **情報レーン** … 各ラベル行と同色の背景に行名。波形左に読み込み中のファイル名。下部レーン左は Music Segment Name／Music Playlist Name
- **小節番号** … 波形先頭基準の相対番号（1 始まり）。アウフタクト時は先頭半端小節が 1
- **テンポ・拍子** … 内部では各位置の値を保持し、表示は値が変わった位置だけ
- **マーカー** … Nuendo／Cubase 単発マーカーを行表示。Entry／Exit Cue より手前に描画する。グループ化された Playlist は、最も若い番号の Playlist を基準に、先頭からの相対位置とコメントを全メンバーで共有する。同期元は通常表示、同期先へ投影されたマーカーは三角だけを 25% の半透明で表示し、コメントは重複表示しない。共有結果はプレビュー表示・Next Cue・Wwise Custom Cue に共通して使う（WAV へは埋め込まない）。途中からグループ化した場合も同じ規則で即時同期し、グループ解除時は各 Playlist 固有の元情報へ戻す
- **リージョン着色** … テンポ変化・拍子変化・サイクル In/Out などで分割。通常グレー／`-L`／`-A`／`-E`／`-R`（`-R` は波形と Music Segment／Playlist レーンの上に重ねる）。`-L` 連続区間はプレビュー再生時に無限ループ。直後が `-E` ならループ頭へ戻る瞬間に Exit を並行ワンショット二重再生（シークで即停止）
- **リージョン境界** … 除外で区切られた連続リージョン固まりの頭／末尾のみに、縦線＋波形上端の半三角（開始＝右半分、終了＝左半分。Entry／Exit と同形）
- **リージョン端フェード** … 白三角を内側へドラッグして非破壊の端フェード（プレビュー表示・再生のみ。ソース WAV は変更しない）。ハンドルから真下へ 1px 白線。フェード範囲を右クリックでカーブ選択（名前・並びは Wwise MusicClip の CurveIn／CurveOut に合わせる。アウト側メニューは立ち下がりアイコン）。**EXPORT 時に分割 WAV へ焼き込む**（破壊編集。MusicClip の Fade Duration／Shape は WAAPI で変更しない）。長さ制限なし。Playlist 遷移フェードとは別物でゲインは重ねがけ。同一波形の Reload／再ドロップ時は Last Session サイドカーから復元。Ctrl+Z／Ctrl+Y で Undo／Redo
- **Entry / Exit Cue** … 固まりの頭が Entry（Marker 段の半三角・開始形。先頭 `-A` ならその直後）、末尾が Exit（Marker 段の半三角・終了形。末尾 `-E` ならその直前）。リージョン境界より手前、通常マーカーより奥に描画
- **Music Segment 名** … 波形下の Music Segment Name レーン（高さは Measure 行と同じ）に、リージョン束ね単位のセグメント名を通常ウェイトで表示。1 Playlist の出力 Segment が 1 件だけなら接尾辞を付けず、複数なら `…_a` / `…_b`（`-A`／`-E` の束ね含む）。Playlist より細かい。時間的に連続するセグメント同士の境だけ、波形背景色の 3 px 縦線を描く（`-R` などの隙間には描かない）
- **Music Playlist 名** … その下の Music Playlist Name レーン（同高さ）に、エクスポートファイル名（拡張子除く）＋` (.wav)`（例: `元名_1 (.wav)`）を太字で表示。無効項目の仮名 `Excluded Region n` はこのレーンには表示しない。複数パート時は Wwise Playlist 名と一致するが、単一パート時に作る Wwise Playlist Container 名は元ファイル名
- **名前レーンの文字** … Segment／Playlist とも各時間範囲の内側に収まるまで縮小。極端に狭い場合は最小 0.5 px とし、それでも収まらなければ横方向を縮めるため、拡大すると全文を確認できる
- **TimeReference** … iXML（`BWF_TIME_REFERENCE_*`）のみ。無い／0 のときは波形先頭＝PPQ 0 扱い
- **読み込み演出** … ラベル → 波形ワイプ → 小節 → マーカー → Playlist／Segment 名の順で重ね表示

波形範囲外のマーカー／サイクルは描画せず、ログの「波形範囲外（無視）」に出します。

### 分割書き出し

- ［EXPORT］が有効になる条件（すべて必須）:
  - 有効な出力パートが 1 件以上ある
  - WAAPI で Wwise に接続できている
  - Wwise 上で作成先オブジェクトが選択されている（未選択時はステータスバーの詳細が赤文字）
  - プロジェクト設定の書き出し先が指定済みで、存在するディレクトリである
  - 書き出し先が接続中 Wwise プロジェクトの `Originals` 配下である
- 条件未達の理由と対象パスはログの `=== Export Preflight ===` に出す（状態が変わったときだけ）
- Ctrl+Shift で無効化した Playlist は書き出しと Wwise インポートから除外。`Compact Num.` がオンなら残った番号を 1 から詰め、オフなら元番号を維持
- 中間のプレイリスト単位 WAV は作らず、Wwise が参照する Music Segment／Track 単位の最終 WAV だけをプロジェクト設定の書き出し先直下へ直接出力する
- 書き出し WAV は音声の切り出しのみ。リージョン／マーカーは WAV へ埋め込まず、Wwise 登録時にインメモリ情報から設定する
- サイクル名の接尾辞: `-R`＝除外、`-L`＝ループ範囲、`-E`＝Wwise の Exit Cue 以降。  
  `-R` / `-L` / `-E`（および内部生成の `-A`）が重なる配置はエラー
- アプリ独自フォロー:
  - アウフタクト判定（冒頭半端小節、または `-R` Out 後の半端小節）のリージョンには `-A` を付与し、波形塗り・Wwise Entry Cue に反映
  - `-L` 連続の直後リージョンに接尾辞が無いとき、`-E` を自動付与（明示の `-E` サイクルを省略可）。波形塗り・Wwise Exit Cue に反映
- 書き出し中は該当パート枠が発光。結果はログの `=== Export ===` 以降に出力

### Wwise インポート（WAAPI）

書き出し完了後、EXPORT 開始時に固定した**選択オブジェクトの下**へ、確認ダイアログを表示せず Music 構造を自動生成します。利用条件・商標については [商標・ライセンス](#商標ライセンス) を参照してください。

- グループ化されていない出力パート 1 つ、またはグループ 1 つを Music Playlist Container 1 つとして扱う
- 2 パート以上のグループは、1 つの Music Segment 内へ複数 Music Track を置く縦レイヤーとして生成する。Playlist／Segment 名はグループ化後の連番へ詰める
- 最終 Playlist が 2 つ以上なら Music Switch Container（元ファイル名）の下に配置する。あわせて State Group（同名）を `\States\Default Work Unit` に作成し、各 State を同名 Playlist に割当。既存 State Group があるときは削除・再作成せず、同一オブジェクトの State 一覧を現在の Playlist 構成へ更新する。Switch のトランジションは既定 Any → Any（名前 `Transition`）を先頭に明示維持し、続けて各 Playlist 向けの Any → Object ルールを載せる（Exit Source At は遷移先 Playlist の記憶値。グループ時は代表パート＝最小番号）。Source Fade-out ON（Time / Offset / Curve は WAAPI 非対応のため手設定）
- **フェードと Wwise トランジション** … 本アプリの Fade In／Fade Out は「いま再生しているソースがどう振る舞うか」を表す設定であり、Wwise トランジションにおける **Source（出ていく側）** の振る舞いに対応する。そのため EXPORT 時も Source Fade-out をオンにする一方、**Destination（次のソース）側の Fade-in チェックボックスは操作しない**。Destination Fade-in は「次のソースがどう振る舞うか」であり、本アプリのフェード設定の対象外だからである
- **リージョン端フェード → 分割 WAV** … 波形上の白三角フェードは、切り出し WAV へゲインを焼き込む（破壊編集）。MusicClip の Fade Duration／Shape は WAAPI で変更しない。Playlist 遷移用の MusicFade 作成とは別経路
- リージョン 1 つ = Music Segment 1 つ。1 Playlist 内で Segment が 1 件だけなら名前の `_a` を省略し、複数なら `_a` `_b` … の連番。ただし:
  - `-A`（アウフタクト）は次のリージョンと同一セグメントにし、Entry Cue より前として扱う
  - `-E` は直前のリージョンと同一セグメントにし、Exit Cue より後として扱う（`-L` 直後への自動付与分も含む）
  - `-L` の付いたセグメントはプレイリスト上で無限ループ
- 各セグメントにはリージョンのテンポ・拍子を設定（Override）。単発マーカーは Custom Cue として付与（WAV メタデータは参照しない）
- トラックは `[Project.*] StreamEnabled`（既定オン）に従いストリーミングを設定。オン時は、各 Playlist の**先頭セグメント内の全トラック**（グループ化レイヤー含む）に Zero latency オン＋Look-ahead 0 を設定。Prefetch Length（`[Project.*] PrefetchLengthMs`、既定 500）は先頭セグメントの先頭トラックのみ。2 番目以降のセグメントは Look-ahead を `[Project.*] LookAheadMs`（既定 500）に設定。オフ時はストリーミング無効で作成し LookAhead／Prefetch／Zero latency は付けない
- 元 WAV から各 Music Segment／Track の範囲を直接切り出し、書き出し先直下の最終 WAV を取り込む
- **Loudness Normalize（任意・既定オフ）** … Wwise 側にも非破壊の Loudness Normalize があるが、本アプリの機能とは無関係。こちらは分割後のセパレート WAV に対する破壊編集（ゲイン焼き込み）である。Wwise 標準の正規化は、レイヤーミュージック（同一 Segment 内の複数 Track）でレイヤー間バランスが崩れるほか、ストリーミングの Prefetch 区間で音量が暴発する恐れがある。LookAhead Time を設定することで暴発を防ぐことが出来るが、ゼロレイテンシーの意味が無くなってしまう。そのため、書き出し時点で ITU-R BS.1770 相当の Integrated Loudness（LKFS）に揃える独自処理を用意した。`Preserve Group Balance`（既定オン）時は、グループ内で最も大きい音量のパートを Target に合わせ、他メンバーへ同じゲインを適用して相対バランスを保つ。オフ時はパートごとに個別正規化する。**Auto Volume（既定オフ）** は、焼き込んだ線形ゲインの逆（dB）を対応する Music Playlist Container の `Make-Up Gain`（既定）または `Voice Volume` へ設定し、再生時の体感音量を元に戻す。選択していない方のプロパティは 0 にする。Normalize オフ時は Auto Volume も適用しない

---

## ソース構成

| パス | 役割 |
|------|------|
| `Program.cs` | エントリポイント |
| `UI/` | フォーム・波形ビュー・ステータスバー・色定義・ウィンドウ／INI 設定 |
| `Processing/` | ドロップ処理（Wave／XML → プレビュー＋ログ） |
| `Nuendo/` | Cubase／Nuendo tracklist XML・テンポ／拍子マップ・小節境界 |
| `Wave/` | WAV／iXML・ピーク・再生・小節／リージョン／分割書き出し（音声のみ） |
| `Wwise/` | WAAPI 接続確認・Music 構造インポート（HTTP） |

名前空間はフォルダに合わせます（例: `MgaWwiseIMImporter.UI`、`MgaWwiseIMImporter.Wave`）。

UI はダーク基調（`UiColors`）。タイトルバーとログまわりもそれに寄せています。

---

## 商標・ライセンス

### Audiokinetic / Wwise / WAAPI

本ツールは Audiokinetic のソフトウェアを同梱・再配布しません。Wwise Authoring へはローカル HTTP 経由の **WAAPI**（JSON 呼び出し）のみで通信します。

- **Wwise®** および **Audiokinetic®** は Audiokinetic Inc. の商標または登録商標です（米国その他）。
- 本ツールは Audiokinetic Inc. と提携・後援・公式認定された製品ではありません。
- WAAPI を利用するには、利用者が有効な **Wwise ライセンス**（プロジェクト登録とライセンスキー）を持つ必要があります。本ツールはそれを代替しません。
- Audiokinetic のロゴやマークは使用していません。アプリ名・説明文での "Wwise" 表記は、対応先を示す説明的な用法です。

アプリ下部には次の英文を表示します。

> © 2026 MIYABI GAME AUDIO INC.  GitHub  
> Wwise® and Audiokinetic® are trademarks of Audiokinetic Inc.

### Steinberg / Nuendo / Cubase

本ツールは Steinberg のソフトウェアを同梱・再配布しません。利用者が Cubase／Nuendo から書き出した **tracklist XML** を読み取るのみです。

- **Nuendo®**、**Cubase®**、および **Steinberg®** は Steinberg Media Technologies GmbH の商標または登録商標です（米国その他）。
- 本ツールは Steinberg Media Technologies GmbH と提携・後援・公式認定された製品ではありません。
- Steinberg のロゴやマークは使用していません。説明文での "Nuendo"／"Cubase" 表記は、対応する書き出し形式を示す説明的な用法です。

### 同梱ライブラリ・フォント

| 名称 | 用途 | ライセンス |
|------|------|------------|
| NAudio | WAV 読み書き・再生 | Microsoft Public License (Ms-PL) |
| UDEV Gothic | ログ表示フォント（exe 埋め込み） | SIL Open Font License 1.1（`Licenses/LICENSE-UDEV-GOTHIC.txt` を同梱） |

---

## 設定ファイル

- `test/` はローカル検証用でリポジトリには含めません（`.gitignore`）

### バージョン番号

csproj の `<Version>`（SemVer）を表示・比較・GitHub タグ照合に共通利用します。

| 項目 | 場所 | 例 |
|------|------|------|
| 版番号 | csproj の `<Version>`（Metadata `AppVersion` / `InformationalVersion`） | `1.0.3-beta` |
| GitHub | Release タグ（先頭 `v` 可） | `v1.0.3-beta` |

起動時に GitHub Releases を照合し、新しい版があればログ（オレンジ）とダイアログで案内します（自動ダウンロードなし。「後で」でその版の再案内をスキップ）。リリース時は **csproj の Version とタグを一致**させてください。

### `MgaWwiseIMImporter.ini`

exe と同じフォルダに置きます（無ければ起動時に既定値で作成／不足キーを追記）。

**注意（再インストール・差し替え）**  
プロジェクト設定・追加マーカー・Keep Last Session などの作業データは、すべて **exe と同じフォルダ** に保存されます。

| ファイル | 主な内容 |
|----------|----------|
| `MgaWwiseIMImporter.ini` | プロジェクト一覧／各プロジェクト設定（Marker Grid・Comment 含む）／アプリ設定／ウィンドウ位置 など |
| `MgaWwiseIMImporter.lastwave.<プロジェクト名>.json` | 追加マーカー位置、Playlist グループ／無効化、パート別 Fade・Exit Source At など |

フォルダごと上書き・削除して入れ替えると、これらのデータも消えます。更新時は exe だけ差し替えるか、上記ファイルを退避してから入れ替えてください。Nuendo／Cubase XML 由来のマーカーはアプリに保存せず、読み込み時に同名 `.xml` から取り込みます。

| セクション | 内容 |
|------------|------|
| `[App]` | アプリ全体設定（Always on Top／表示言語） |
| `[Projects]` / `[Project.*]` | プロジェクト一覧・プロジェクト別設定（Keep Target 含む） |
| `[Window]` | 位置・サイズ |

#### `[App]`

アプリ全体の設定です。プロジェクト切替・CLEAR・新規プロジェクトでは変わりません。

| キー | 意味 | 既定 |
|------|------|------|
| `AlwaysOnTop` | ウィンドウを最前面に固定（`1`/`0`） | `0` |
| `UiLanguage` | UI／ツールチップ／ダイアログ／ログの表示言語（`ja` / `en`）。プロジェクトバー右の JP／EN ボタンでも切替 | `ja` |
| `SkippedUpdateVersion` | アップデート案内で「後で」を選んだリモート版（SemVer）。より新しい版が出るまで再案内しない | （空） |

#### `[Project.*]`

サイドカー JSON（`MgaWwiseIMImporter.lastwave.<プロジェクト名>.json`）はプロジェクト単位で、グループ／無効化／追加マーカー／Fade・Exit Source At を保持します。キーは UI に近い順です。

| キー | 意味 | 既定 |
|------|------|------|
| `Name` | プロジェクト表示名 | （セクション名に対応） |
| `KeepLastSession` | 起動時／このプロジェクトへ戻ったときに最後のセッションを復元（`1`/`0`） | `1` |
| `LastWavePath` | 最後に正常に読み込んだ波形のフルパス | （空） |
| `OutputDirectory` | 分割 WAV の書き出し先 | （空） |
| `KeepTarget` | 作成先パスをこのプロジェクトで固定し、EXPORT にもそのパスを使う（`1`/`0`） | `0` |
| `KeptTargetPath` | 記憶中の Wwise オブジェクトパス | （空） |
| `KeptTargetProjectFilePath` | 記憶時の Wwise プロジェクトファイルパス（不一致なら再選択しない） | （空） |
| `FadeInSeconds` / `FadeOutSeconds` | パート未設定時の Fade In／Out 秒数（`0`＝None） | `0` |
| `ExitSourceAt` | パート未設定時の Exit Source At（`Immediate`／`NextBar`／`NextBeat`／`NextCue`／`ExitCue`） | `Immediate` |
| `CompactFileNumbers` | 無効項目を除いた書き出し番号を詰める（`1`/`0`。Compact Num.） | `0` |
| `MoreOptionsExpanded` | More Options パネルを開いた状態にする（`1`/`0`） | `1` |
| `StreamEnabled` | Music Track のストリーミング有効（`1`/`0`。Stream） | `1` |
| `PrefetchLengthMs` | Playlist 先頭セグメント先頭トラックの Prefetch Length（ms、0〜9999。Stream オン時） | `500` |
| `LookAheadMs` | 2 番目以降のセグメントの Look-ahead Time（ms、0〜9999。Stream オン時） | `500` |
| `LoudnessNormalizeEnabled` | EXPORT 時に分割 WAV へラウドネス正規化（破壊編集）を行う（`1`/`0`。Normalize） | `0` |
| `LoudnessTargetLkfs` | 正規化ターゲット（LKFS、−70〜0） | `-24` |
| `LoudnessPreserveGroupBalance` | グループ内の相対バランスを保って正規化する（`1`/`0`） | `1` |
| `AutoVolumeEnabled` | 正規化ゲインの逆を Music Playlist へ書き戻す（`1`/`0`。Auto Volume） | `0` |
| `AutoVolumeTarget` | Auto Volume の書き戻し先（`MakeUpGain`／`VoiceVolume`） | `MakeUpGain` |
| `GridOverride` | マーカー付与のスナップ単位（`Bar`／`Beat`／`Default`＝UI の Timeline。Marker Grid） | `Bar` |
| `CommentDigits` | マーカーコメント連番の桁数（0〜6。0 で連番なし） | `3` |
| `CommentZeroPad` | 連番を桁数まで 0 埋めする（`1`/`0`） | `1` |
| `CommentResetPerPart` | パートごとに連番をリセットする（`1`/`0`） | `1` |
| `CommentPrefixEnabled` / `CommentPrefix` | 接頭語の有効と文字列 | `0`／（空） |
| `CommentSuffixEnabled` / `CommentSuffix` | 接尾語の有効と文字列 | `0`／（空） |
| `CommentJoinerEnabled` / `CommentJoiner` | Separator（区切り）の有効と文字列 | `0`／（空） |

WAAPI の URL／タイムアウト／起動プローブ、および State Group 親パスはアプリ内固定です。
