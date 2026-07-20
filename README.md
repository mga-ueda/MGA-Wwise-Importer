# MGA Wwise IMImporter

Nuendo の tracklist XML と Wave を読み、波形プレビュー、分割 WAV の書き出し、WAAPI 経由の Wwise Music 構造インポートを行う Windows 向けツール（開発中）です。

## 使い方

`.wav` または同名ペアの `.xml` をドロップします（波形には `.wav` が必須）。同名 `.xml` があれば小節・テンポ・拍子・マーカー・リージョンを重ねます。`Load Last Wave` がオンなら、起動時に選択中のプロジェクトで最後に読み込んだ波形を自動読み込みします。マルチチャンネル（クアッド／5ch など Extensible 形式）の WAV にも対応し、プレビュー再生はステレオへダウンミックスされます。

出力パートがあり、かつ書き出し条件（Wwise 接続・作成先選択・プロジェクト書き出し先が接続中プロジェクトの Originals 配下）を満たすとき、画面下部の操作バー上の［EXPORT］が有効になります（条件未達の理由はログに出します）。

| 操作 | 内容 |
|------|------|
| G | 現在位置以前で最も近い小節番号を初期表示。1 以上の整数を Enter で確定してジャンプし、Esc でキャンセル。存在しない番号はログへ通知 |
| Space | 再生／一時停止。`-L` 区間内（または再生で突入）では末尾で先頭へ無限ループ（サンプル単位で継ぎ目なし）。直後が `-E` なら、ループ頭へ戻る瞬間に Exit をワンショット二重再生（赤のシークバー／軌跡で可視化・Wwise 相当）。別位置へシークするとループ／Exit とも直ちに解除（行き先が別の `-L` ならその区間に付け替え）。シアンのシークバーが追従し、軌跡として残光を残す（停止時は消す）。ズーム中は画面外へ出たらページめくりで表示追従 |
| ↑ / ↓ / ホイール | 時間軸の拡大／縮小（既定＝全体表示より縮小しない） |
| Ctrl+↑ / Ctrl+↓ | 時間軸を最大倍率／既定（全体表示）に |
| Shift+ホイール | 拡大中の時間軸を左右へスクロール |
| 波形下部スクロールバー | 時間軸の表示位置を移動。全体表示中も細いダークトラックを常時表示 |
| Shift+↑ / Shift+↓ / Ctrl+ホイール | 縦方向（振幅）の拡大／縮小（既定より縮小しない） |
| Ctrl+Shift+↑ / Ctrl+Shift+↓ | 縦方向を最大倍率／既定に |
| PageUp / PageDown | 再生位置を現在の表示幅 1 画面分だけ前／次へジャンプ（時間量ではなく現在の画面幅が単位。PageUp は押し続けている間は再生を止め、離すと再開） |
| Home / End | 再生位置を前／次の小節線へ（Home は押し続けている間は再生を止め、離すと再開） |
| Ctrl+← / Ctrl+→ | 再生位置を前／次のリージョン分割点へ（← は押し続けている間は再生を止め、離すと再開） |
| Ctrl+Home / Ctrl+End | 再生位置と表示窓を波形冒頭／末尾へ（Home 側は押し続けている間は再生を止め、離すと再開） |
| 波形上のマウス | `MouseGuide`（既定は半透明の白）の縦線でマウス位置を表示 |
| Shift＋Markerレーンを左ドラッグ | Music Playlist 範囲内の表示中グリッドへマーカーを追加（Playlist範囲外および `-A`／`-E` 区間には追加しない）。8小節未満まで拡大した表示では拍、それ以外では画面に描画されている小節線へスナップ。追加分は位置順に `001`、`002`…の3桁数値コメントを付け、現在の読み込み中だけ保持し、Next Cue・Wwise Custom Cueにも使用する（WAV へは埋め込まない）。グループ化された Playlist 上で追加した場合は、グループ内で最も若い番号の Playlist 上の同じ相対位置へ記録し、全メンバーへ共有する。`-A`／`-E` 区間のCustom CueはWwise上で無視されるため、アプリ側で追加対象外にしている |
| Ctrl＋Markerレーンを左ドラッグ | この操作で追加したマーカーだけを消去。グループ化された Playlist では全メンバーの同じ共有マーカーを消去する。XML由来マーカーは変更しない |
| Shift＋Playlist 項目を左クリック／ドラッグ | Playlist をグループ化し、縦レイヤーとして扱う。インジケーターだけでなく Playlist 名部分でも操作可能。既に別グループへ属していても解除せず上書きできる。同じグループのマーカー情報は共有され、途中からグループ化した場合はグループ内で最も若い番号の Playlist のマーカー位置・コメントへ同期する。`Reset Per Part` 無効時の全体連番も、同期先の重複マーカーを数えず基準 Playlist 側だけを対象にする |
| Ctrl＋Playlist 項目を左クリック／ドラッグ | Playlist のグループ化を解除する。解除後は各 Playlist がグループ化前から保持していたマーカー情報へ戻る |
| Ctrl＋Shift＋Playlist 項目を左クリック／ドラッグ | Playlist を書き出し対象外へ切り替え、同じ操作で再有効化する。無効項目は一覧で `Excluded Region n` の赤文字になり、波形だけを25%表示にする。再生・遷移・グループ化・マーカー編集・WAV書き出し・Wwiseインポートの対象外 |
| 拍グリッド | 表示範囲が8小節未満まで拡大されたとき、小節線より薄い拍線を表示。Measure／Music Segment Name／Music Playlist Nameレーンには描画しない |
| 波形クリック／ドラッグ | 再生位置を移動 |
| タイムラインをダブルクリック | 表示範囲と交差する Music Playlist がちょうど 1 つなら全体表示へ戻す。複数ある場合は、マウス直下の Playlist 範囲を表示幅の 90% にして中央表示。直下に Playlist がなければズームしないが、通常クリック相当のシークは発生 |
| 波形ファイル名 | 左側情報欄の幅をファイル名が一行で収まるよう自動調整し、名前の右側に再生出力の縦型レベルメーターを表示。最小／最大色のグラデーションは色設定から変更可能 |
| 波形ファイル名をダブルクリック | 表示位置に直接現れる入力欄で、拡張子なしの基底名を編集。Enter またはフォーカス移動で確定、Esc でキャンセル。変更は直ちに Playlist／Segment 表示へ反映され、書き出す WAV ファイル名と Wwise の Container／Playlist／Segment／Track 名にも使用 |
| Fade In | Playlist左側のラジオボタンから遷移先のフェードイン時間を `None / 1.0 / 3.0 / 6.0 / 9.0 Sec.` で選択（既定はNone）。Playlist をクリックしてから選択するとパート（グループ）単位で記憶。再生中のPlaylist遷移だけに適用し、停止中からの開始には適用しない。遷移先が `-A` なら先行再生開始からフェードインし、Entry Cue後も同じ包絡線を継続。変更時点ですでに待機中の遷移には影響せず、次の遷移予約から適用 |
| Fade Out | Playlist左側のラジオボタンから遷移元のフェードアウト時間を `None / 1.0 / 3.0 / 6.0 / 9.0 Sec.` で選択（既定はNone）。Playlist をクリックしてから選択するとパート（グループ）単位で記憶。変更時点ですでに待機中の遷移には影響せず、次の遷移予約から適用 |
| More Options | 右側パネル下部の折りたたみ（既定開き）。開くと Stream／Loudness Normalize／Marker Grid／Marker Comment を表示。開閉状態は `[Project.*] MoreOptionsExpanded` に自動保存。開閉時はウィンドウ高さを調整し Music Playlist の高さは維持 |
| Stream | More Options 内・上段左。`Stream` チェック（既定オン）と `Look-ahead Time`／`Prefetch Length`（ms、0〜9999、既定 500）。`[Project.*] StreamEnabled`／`LookAheadMs`／`PrefetchLengthMs` に自動保存。Stream オフ時は Look-ahead Time／Prefetch Length は無効で、Wwise へもストリーミングなしで作成 |
| Loudness Normalize | More Options 内・上段右。`Normalize`（既定オフ）／`Target` LKFS（既定 −24）／`Preserve Group Balance`（既定オン）。Wwise の非破壊 Loudness Normalize とは別の、このアプリ独自機能。EXPORT 時に分割 WAV へ破壊編集でゲインを焼き込む。`[Project.*] LoudnessNormalizeEnabled`／`LoudnessTargetLkfs`／`LoudnessPreserveGroupBalance` に自動保存 |
| Marker Grid / Marker Comment | More Options 内・下段。スナップ単位（Bar／Beat／Timeline）とコメント桁・0埋め・接頭／接尾／区切り・Reset Per Part。変更は選択中プロジェクトの `[Project.*]` へ自動保存（旧グローバル `[Markers]` は起動時に除去） |
| Exit Source At | Fade Out 右のラジオボタン。Playlist をクリックしてから選択すると、その Playlist（パート）ごとに Fade In／Fade Out／Group Fade／Exit Source At を記憶する。Fade In／Out 内は通常候補の下に `Group` 見出しでグループ用候補を区分。通常 Fade・Exit Source・Group Fade はいずれも同一グループ ID で共通同期。同一グループ内遷移は Group Fade のみ（通常 Fade は無効）。グループ外からの遷移は通常の Fade In/Out を使用。Last Wave サイドカーへオートセーブ／復元。Wwise へは Exit Source At のみ渡し、Group Fade は未使用 |
| 遷移先同期（自動） | 同一グループ内の遷移は `Same Time`、異なるグループ間または未グループとの遷移は `Entry Cue` を自動選択する。`Same Time` は実際の退出時点における現在 Playlist 先頭からの経過時間を遷移先へ引き継ぐ。引継ぎ位置が遷移先の長さ以上なら予約しない |
| Music Playlist 一覧 | ログ右側の `Music Playlist` 一覧から遷移先を選択。一覧幅はファイル名が改行されない必要幅へ自動可変（表示領域を超える場合だけ省略表示）。停止／一時停止中は対象の頭から即再生し、再生中は `Exit Source At` の位置で遷移。同一グループ内の `Same Time` では相対位置から直接開始し、それ以外の `Entry Cue` で遷移先冒頭が `-A` の場合は、その長さだけ先行して重ねる。Next Bar／Next Beatでは境界に `-A` 終端を揃え、Next Cue／Exit Cueまでの時間が足りない場合は `-A` を即開始して退出だけを指定 Cue に揃える。Immediateでは旧曲フェードと `-A` を同時に即開始する。先行再生中の位置と残像は緑色、フェードアウト中は白、`-E` 到達後は赤で表示する。通常時は枠なし、再生中は背景を塗り、遷移完了時は枠をフェード発光する。遷移待機中は予約先の枠がテンポ／拍子に同期して点滅する |
| Esc | 通常画面では終了確認を表示。小節ジャンプではキャンセル、`色調整（開発者）` パネルでは閉じる |
| 最大化 | ウィンドウの最大化が可能 |
| ドロップ | `.wav`（＋同名 `.xml`）を読み込み、プレビュー更新。書き出し条件を満たせば［EXPORT］を有効化 |
| ログ右下アイコン | クリアアイコンでログ表示だけを消去、コピーアイコンで全ログをクリップボードへコピー、保存アイコンで UTF-8 の `.log`／`.txt` ファイルへ保存（各機能名はツールチップに表示） |
| Debug Log | 操作バーのチェック。Playlist推移・再生エンジン診断を `[PlaybackDebug]` JSON Lines形式でログへ出力（既定オン） |
| Compact File Numbers | 無効項目を除いた書き出し WAV・Playlist・Segment 名の番号を詰める（既定オン）。オフでは元の番号を維持し、欠番を残す |
| Load Last Wave | `Always on Top` 左側のチェック。起動時に、選択中のプロジェクトで最後に正常に読み込んだ波形を再読み込み（`[Project.*] LoadLastWaveOnStartup` / `LastWavePath`）。あわせてサイドカー JSON のグループ／無効化／追加マーカー／Fade In・Out（通常／Group）／Exit Source At（パート別）を復元 |
| Always on Top | WAAPI ステータスバー直上の操作バーにあるチェック。ウィンドウを最前面に固定（`[Project.*] AlwaysOnTop`。既定オフ） |
| Keep Target | WAAPI ステータスバー右端のチェック。オンにした時点の作成先をアプリ側で固定し、その後 Wwise で選択を変えても表示・EXPORT 先は変わらない。接続中は Keep 先へ書き出す旨を警告色で表示。未接続時は赤字のエラー表示（チェックは外さない）。再接続できたら記憶パスを有効化。起動時／EXPORT 前は可能なら Wwise 上でも同パスを再選択（`[Project.*] KeepTarget` / `KeptTargetPath`。既定オフ） |
| RELOAD | 最後にドロップまたは自動読み込みした WAV／XML を元ファイルから再読み込みする。ログ・Playlist のグループ化・無効化・Exit Source At 記憶・追加マーカーはリセット。再生は一時コピー経由のため、読み込み中も外部アプリは元 WAV を上書き可能 |
| EXPORT | WAAPI ステータスバー直上の操作バーにある青ボタン。プロジェクト書き出し先へ分割 WAV を書き出し、続けて Wwise へ登録。Wwise 未接続／作成先未選択／書き出し先が未指定・不存在・Originals 外のときは無効 |
| Ctrl+Shift+C | `色調整（開発者）` パネル（`[Colors]` INI。**DEBUG のみ**） |

### プレビュー表示

上部にラベル行（小節番号／テンポ／拍子／マーカー）、左に行名（Measure／Tempo／Signature／Marker／Music Segment Name／Music Playlist Name）と波形ファイル名、中央に波形、下に各名前レーンです。

- **情報レーン** … 各ラベル行と同色の背景に行名。波形左に読み込み中のファイル名。下部レーン左は Music Segment Name／Music Playlist Name
- **小節番号** … 波形先頭基準の相対番号（1 始まり）。アウフタクト時は先頭半端小節が 1
- **テンポ・拍子** … 内部では各位置の値を保持し、表示は値が変わった位置だけ
- **マーカー** … Nuendo 単発マーカーを行表示。グループ化された Playlist は、最も若い番号の Playlist を基準に、先頭からの相対位置とコメントを全メンバーで共有する。同期元は通常表示、同期先へ投影されたマーカーは三角だけを 25% の半透明で表示し、コメントは重複表示しない。共有結果はプレビュー表示・Next Cue・Wwise Custom Cue に共通して使う（WAV へは埋め込まない）。途中からグループ化した場合も同じ規則で即時同期し、グループ解除時は各 Playlist 固有の元情報へ戻す
- **リージョン着色** … テンポ変化・拍子変化・サイクル In/Out などで分割。通常グレー／`-L`／`-A`／`-E`／`-R` は `[Colors]` の直値で塗る（`-R` は波形と Music Segment／Playlist レーンの上に重ねる）。`-L` 連続区間はプレビュー再生時に無限ループ。直後が `-E` ならループ頭へ戻る瞬間に Exit を並行ワンショット二重再生（シークで即停止）
- **リージョン境界** … 除外で区切られた連続リージョン固まりの頭／末尾、および Wwise Music Segment の分かれ目に `RegionBoundaryMarker`（既定は明るいグレー）の縦線＋下部の半四角（開始＝右半分、終了＝左半分）
- **Entry / Exit Cue** … 固まりの頭が Entry（`EntryCueMarker`、既定はライム。上部半三角・開始形。先頭 `-A` ならその直後）、末尾が Exit（`ExitCueMarker`、既定は赤。上部半三角・終了形。末尾 `-E` ならその直前）。リージョン境界より手前に描画
- **Music Segment 名** … 波形下の Music Segment Name レーン（高さは Measure 行と同じ）に、リージョン束ね単位のセグメント名を通常ウェイトで表示。1 Playlist の出力 Segment が1件だけなら接尾辞を付けず、複数なら `…_a` / `…_b`（`-A`／`-E` の束ね含む）とする。Playlist より細かい。時間的に連続するセグメント同士の境だけ、波形背景色の 3 px 縦線を描く（`-R` などの隙間には描かない）
- **Music Playlist 名** … その下の Music Playlist Name レーン（同高さ）に、エクスポートファイル名（拡張子除く）＋` (.wav)`（例: `元名_1 (.wav)`）を太字で表示。無効項目の仮名 `Excluded Region n` はこのレーンには表示しない。複数パート時は Wwise Playlist 名と一致するが、単一パート時に作る Wwise Playlist Container 名は元ファイル名
- **名前レーンの文字** … Segment／Playlist とも各時間範囲の内側に収まるまで縮小。極端に狭い場合は最小 0.5 px とし、それでも収まらなければ横方向を縮めるため、拡大すると全文を確認できる
- **Music Playlist 一覧（遷移シミュレーター）** … ログ右側に出力パート由来の Playlist 名を縦表示。件数が多い場合はスクロールし、長い名前は省略表示してホバーで全文を表示する。項目クリックでその Playlist の Fade／Exit Source At をラジオへ反映。再生中の選択は遷移先 Playlist の Exit Source At（Immediate／Next Bar／Next Beat／Next Cue／Exit Cue）と Fade へクオンタイズし、同一グループ内は Same Time、それ以外は Entry Cue で遷移先位置を自動決定する。Same Timeはボタン押下時ではなく実際の退出時点の相対時間を使う。`-L` ループ中でも到達可能な境界では Playlist 遷移を優先し、Next Cueは現在周回のループ終端までに次の単発マーカーがなければ予約しない。遷移待ち中の再選択は最後に選んだ Playlist を採用する。波形側でシークすると自動状態と予約を解除し、現在位置の Playlist を手動再生色へ切り替える
- **右側パネルの高さ** … Music Playlist（Compact Num. 含む）の下端は Fade In セクション下端に揃える。その下に More Options（折りたたみ。Stream／Loudness／Marker Grid／Marker Comment を内包）。開閉時はウィンドウ高さを増減して Playlist 高さを維持する。ウィンドウを高くしても Playlist は Fade In 高のまま（余剰はログ側）
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
- Ctrl+Shift で無効化した Playlist は書き出しと Wwise インポートから除外。`Compact File Numbers` がオンなら残った番号を1から詰め、オフなら元番号を維持
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
- 最終 Playlist が 2 つ以上なら Music Switch Container（元ファイル名）の下に配置する。あわせて State Group（同名）を `\States\Default Work Unit` に作成し、各 Stateを同名 Playlist に割当。既存 State Group があるときは削除・再作成せず、同一オブジェクトの State 一覧を現在の Playlist 構成へ更新する。Switch のトランジションは既定 Any → Any（名前 `Transition`）を先頭に明示維持し、続けて各 Playlist 向けの Any → Object ルールを載せる（Exit Source At は遷移先 Playlist の記憶値。グループ時は代表パート＝最小番号）。Source Fade-out ON（Time / Offset / Curve は WAAPI 非対応のため手設定）
- リージョン 1 つ = Music Segment 1 つ。1 Playlist 内で Segment が1件だけなら名前の `_a` を省略し、複数なら `_a` `_b` … の連番。ただし:
  - `-A`（アウフタクト）は次のリージョンと同一セグメントにし、Entry Cue より前として扱う
  - `-E` は直前のリージョンと同一セグメントにし、Exit Cue より後として扱う（`-L` 直後への自動付与分も含む）
  - `-L` の付いたセグメントはプレイリスト上で無限ループ
- 各セグメントにはリージョンのテンポ・拍子を設定（Override）。単発マーカーは Custom Cue として付与（WAV メタデータは参照しない）
- トラックは `[Project.*] StreamEnabled`（既定オン）に従いストリーミングを設定。オン時は、各 Playlist の先頭セグメントかつ先頭トラックに Prefetch Length（`[Project.*] PrefetchLengthMs`、既定 500）と Zero latency オン＋Look-ahead 0 を設定。2 番目以降のセグメント／トラックは Look-ahead を `[Project.*] LookAheadMs`（既定 500）に設定。オフ時はストリーミング無効で作成し LookAhead／Prefetch／Zero latency は付けない
- 元 WAV から各 Music Segment／Track の範囲を直接切り出し、書き出し先直下の最終 WAV を取り込む
- **Loudness Normalize（任意・既定オフ）** … Wwise 側にも非破壊の Loudness Normalize があるが、本アプリの機能とは無関係。こちらは分割後のセパレート WAV に対する破壊編集（ゲイン焼き込み）である。Wwise 標準の正規化は、レイヤーミュージック（同一 Segment 内の複数 Track）でレイヤー間バランスが崩れるほか、ストリーミングの Prefetch 区間で音量が暴発する恐れがある。LookAhead Time を設定することで暴発を防ぐことが出来るが、ゼロレイテンシーの意味が無くなってしまう。そのため、書き出し時点で ITU-R BS.1770 相当の Integrated Loudness（LKFS）に揃える独自処理を用意した。`Preserve Group Balance`（既定オン）時は、グループ内で最も大きい音量のパートを Target に合わせ、他メンバーへ同じゲインを適用して相対バランスを保つ。オフ時はパートごとに個別正規化する

---

## ソース構成

| パス | 役割 |
|------|------|
| `Program.cs` | エントリポイント |
| `UI/` | フォーム・波形ビュー・ステータスバー・色定義・ウィンドウ／INI 設定 |
| `Processing/` | ドロップ処理（Wave／XML → プレビュー＋ログ） |
| `Nuendo/` | tracklist XML・テンポ／拍子マップ・小節境界 |
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

> Wwise® and Audiokinetic® are trademarks of Audiokinetic Inc.

### 同梱ライブラリ・フォント

| 名称 | 用途 | ライセンス |
|------|------|------------|
| NAudio | WAV 読み書き・再生 | Microsoft Public License (Ms-PL) |
| UDEV Gothic | ログ表示フォント | SIL Open Font License 1.1（`Fonts/LICENSE-UDEV-GOTHIC.txt` を同梱） |

---

## 開発メモ

- `test/` はローカル検証用でリポジトリには含めません（`.gitignore`）

### `MgaWwiseIMImporter.ini`

exe と同じフォルダに置きます（無ければ起動時に既定値で作成／不足キーを追記）。

| セクション | 内容 |
|------------|------|
| `[Developer]` | 診断ログ |
| `[Projects]` / `[Project.*]` | プロジェクト一覧・プロジェクト別設定 |
| `[Window]` | 位置・サイズ |
| `[Colors]` | UI 色（**DEBUG のみ**。Ctrl+Shift+C パネル。リリースではコード既定のみで INI に書かない） |

#### `[Developer]`

| キー | 意味 | 既定 |
|------|------|------|
| `DetailedPlaybackLog` | Playlist推移・transport操作・ループ／Exit・音声プロバイダー内部遷移をJSON Linesで記録（`1`/`0`） | `1` |

```ini
[Developer]
DetailedPlaybackLog=1
```

旧キー `TopMost`、`AutoLoadWavePath`、`AutoLoadOnStartup` は起動時に除去されます。

#### `[Project.*]`

`LoadLastWaveOnStartup` は `Load Last Wave` のオン／オフ、`LastWavePath` は最後に正常に読み込んだ波形のフルパスです。どちらもプロジェクト単位で自動保存されます。

| キー | 意味 | 既定 |
|------|------|------|
| `KeepTarget` | 作成先パスをアプリ側で固定し、EXPORT にもそのパスを使う（`1`/`0`。ステータスバー右の Keep Target）。再起動後も保持 | `0` |
| `KeptTargetPath` | 記憶中の Wwise オブジェクトパス | （空） |
| `KeptTargetProjectFilePath` | 記憶時のプロジェクトファイルパス（不一致なら再選択しない） | （空） |
| `StreamEnabled` | Music Track のストリーミング有効（`1`/`0`。Stream 列のチェック） | `1` |
| `LookAheadMs` | 2 番目以降のセグメントの Look-ahead time（ms、0〜9999。Stream オン時） | `500` |
| `PrefetchLengthMs` | Playlist 先頭セグメント先頭トラックの Prefetch Length（ms、0〜9999。Stream オン時） | `500` |
| `LoudnessNormalizeEnabled` | EXPORT 時に分割 WAV へラウドネス正規化（破壊編集）を行う（`1`/`0`。Normalize チェック） | `0` |
| `LoudnessTargetLkfs` | 正規化ターゲット（LKFS、−70〜0） | `-24` |
| `LoudnessPreserveGroupBalance` | グループ内の相対バランスを保って正規化する（`1`/`0`） | `1` |
| `MoreOptionsExpanded` | More Options パネルを開いた状態にする（`1`/`0`） | `1` |
| `GridOverride` | マーカー付与のスナップ単位（`Default`／`Bar`／`Beat`。Marker Grid） | `Default` |
| `CommentDigits` | マーカーコメント連番の桁数（0〜6。0 で連番なし） | `3` |
| `CommentZeroPad` | 連番を桁数まで 0 埋めする（`1`/`0`） | `1` |
| `CommentPrefix` / `CommentSuffix` / `CommentJoiner` | コメントの接頭語・接尾語・区切り文字（空なら無効） | （空） |
| `CommentResetPerPart` | パートごとに連番をリセットする（`1`/`0`） | `1` |

旧 `[Waapi]` の Keep Target 同名キーは起動時にアクティブプロジェクトへ移行し、セクション全体を除去します（WAAPI の URL／タイムアウト／起動プローブはアプリ内固定）。旧 `[WwiseImport]`（`StateGroupParentPath`／`LookAheadMs`／`PrefetchLengthMs`）も起動時に移行・除去します（State Group 親パスはアプリ内固定、LookAhead／Prefetch は各 `[Project.*]`）。旧グローバル `[Markers]` も起動時に除去されます（値は各 `[Project.*]` 側が正）。

#### `[Colors]`（DEBUG のみ）

UI 色は Ctrl+Shift+C のモードレスな開発者パネルから編集できます。スウォッチまたは RGB 値を変更すると即時反映・INI 保存され、Enter またはフォーカス移動で確定、Esc で閉じます。値は `#RRGGBB` 形式で保存し、アルファ値は各色のコード既定値を使用します。Playlist の背景・通常／ホバー／再生中文字・自動／手動再生背景は `Playlist*` キーで個別編集でき、既存 INI にないキーは起動時に自動追記します。波形リージョン塗り（`RegionWaveFill*`）は波形エリアに直値で適用され、`RegionWaveFillExcluded` は Music Segment／Playlist レーンにも重ねて表示されます。

**リリースビルド**では色パネルが無いため、コード既定色のみを使い、起動時に `[Colors]` セクションを除去します。
