# TailEMG PoC

MyoWare 2 の筋電値を VRChat の Avatar Parameter (OSC) へ送る PoC。仕様は依頼書.txt(リポジトリには含めずローカル管理)。

## 現在の進捗

| Milestone | 内容 | 状態 |
|---|---|---|
| A | WPF / FakeEmgInput / Signal Processing / OSC | **完了**(VRChat 疎通確認済み) |
| B | BLE Device Discovery / Diagnostic UI | **実装済み**(実機確認待ち) |
| C | MyoWareBleInput(Notification 受信・parse・Pipeline 接続・簡易 Reconnect) | **実装済み**(実機確認待ち) |
| D | Calibration 操作 / CSV Logging / Chart / Settings 保存 / Reconnect | **実装済み**(Fake で動作確認済み。BLE 切断中の挙動は実機確認待ち) |

## 構成

```
TailEmg.sln
src/TailEmg.Core      Input / Processing(Calibration 含む) / Osc / Ble(モデル・UUID・payload parser・GATT レポート) / Logging(CSV) / Settings(JSON)  Windows API 非依存
src/TailEmg.Windows   BLE 実装(BleScanner / GattExplorer / MyoWareBleInput)  TFM: net8.0-windows10.0.19041.0
src/TailEmg.App       WPF + MVVM
tests/TailEmg.Core.Tests   xUnit
```

NuGet 追加なし(BLE は Windows 標準 WinRT API。TFM の `10.0.19041.0` で参照できる)。

処理経路: `IEmgInput(Fake / MyoWareBle) → EmgPipeline(Normalize → Deadzone → Curve → EMA) → OscPublisher(30Hz)`。
BLE も Fake と同じ `IEmgInput.SampleReceived` から `EmgPipeline` へ入る。Signal Processing / OSC は共通で複製していない。

UUID は [BleModels.cs](src/TailEmg.Core/Ble/BleModels.cs) の `BleNotifyProfile.MyoWare` に集約:

- Service `EC3AF789-2154-49F4-A9FC-BC6C88E9E930`
- Characteristic `F3A56EDF-8F1E-4533-93BF-5601B2E91308`(Read / Notify)

## 必要環境

- Windows 10/11(BLE を使うなら Bluetooth 4.0+ アダプタ)
- .NET 8 SDK

## 起動

```powershell
dotnet run --project src/TailEmg.App
dotnet test
```

## Fake Input でのテスト

1. Input Source = `Fake`。Mode を Manual(スライダー 0–4095)か Sine(0→4095→0、既定 4 秒)にする。
2. **Start** → Output の Normalized / Filtered / OSC が動く。
3. REST / MAX は既定 0 / 4095(手入力も可。下の「キャリブレーション」参照)。Deadzone(0.05) / Curve / Gamma(1.0) / Smoothing α(0.25) はリアルタイム反映。
4. **Stop** またはウィンドウを閉じると約 200ms で 0 へ戻し、最後に `0.0` を 1 回送信する。

## キャリブレーション

入力(Fake / BLE)が動いている状態で使う。サンプルが届いていないと、約 6 秒後に「no samples received」になる。

- **Calibrate REST (3 s)**: 筋肉を脱力した状態で押す。3 秒間の **中央値** を REST にする。
- **Calibrate MAX (3 s)**: 押している間、思い切り力を入れる。3 秒間の **95 パーセンタイル** を MAX にする(1 回のスパイクは MAX にならない)。
- MAX ≤ REST のときは赤い警告が出て、Normalize は 0 を返す(OSC は 0 のまま)。
- 値は手入力でも直せる。

## グラフ

画面右に直近 10 秒を表示する(外部ライブラリなし、自前描画)。

- 上: Raw(0–4095)。REST / MAX が破線で重なる。
- 下: Normalized / Filtered / OSC(0–1)。
- 入力が途切れる(BLE 切断など)と Raw / Normalized / Filtered は線が途切れる。OSC は 0 へ戻る様子が見える。

## CSV ログ

**Start Logging** で記録開始、**Stop Logging** で終了。**Open Folder** で保存先を開く。

- 保存先: `%LOCALAPPDATA%\TailEmg\Logs\tailemg_yyyyMMdd_HHmmss.csv`(OneDrive 同期の対象外)
- 列: `Timestamp, BleRawText, RawValue, Normalized, DeadzoneOutput, CurveOutput, FilteredOutput, OscOutput, ConnectionState`
- **入力サンプルごとに 1 行**。BLE は約 700Hz なので、1 分で約 4 万行(数 MB)になる。
- 書き込みは別スレッド。ディスクが追いつかないときは行を捨てて(画面に dropped と出る)、BLE / OSC は止めない。
- Fake のときの `ConnectionState` は `Fake`、`BleRawText` は空。

## 設定の保存

`%APPDATA%\TailEmg\settings.json` に保存する(約 2 秒ごとに変更を検出して保存。終了時にも保存)。

保存する項目: OSC の Host / Port / Address / Rate、Deadzone、Curve、Gamma、Smoothing、REST、MAX、Input Source、最後に使った BLE デバイス。
次回起動時に復元し、Scan で同じデバイスが見つかると自動で選択される。

- ファイルが壊れていたら既定値で起動する(壊れたファイルは `settings.json.bad` に残す)。
- 範囲外の値(例: Port=99999)は、その項目だけ既定値に戻す。
- 初期化したいときは `settings.json` を削除する。

## 実機 MyoWare の接続手順

最初は人体に装着しなくても、BLE 通信の成立確認だけできる。

1. Wireless Shield の電源を ON(Sensor を載せた状態)。Windows の Bluetooth を ON。
2. アプリを起動し Input Source = `MyoWareBle`。
3. **Scan**。一覧の先頭に `[MyoWare svc]` が付いた行が出る(Service UUID を広告しているデバイス)。名前だけ一致する場合は `[name only]`。
   - 一覧は `Name / Address / RSSI`。MyoWare 候補は先頭に並ぶ。
4. デバイスを選択して **GATT Discover**(BLE Diagnostic)。
   - 実機が公開している Service / Characteristic / Properties の一覧が出る。
   - 期待する Service / Characteristic には `<== expected ...` が付き、末尾に found / NOT FOUND が出る。
5. **Connect**。Status が `Connecting → Connected` になる。
6. `Notifications` ログに `len / Hex / "テキスト" -> 数値` が流れる。`Raw` と `Last Sample` が更新される。
7. **Disconnect** で購読を解除して切断(自動再接続しない)。

BLE ではつながるまで OSC は 0 のまま。つながると Fake と同じ経路で OSC に流れる。

### 切断時の挙動

- 接続が切れる(電源 OFF・圏外)と Status が `Reconnecting` になり、1s → 2s → 5s → 5s… の間隔で再接続を試す。
- 切断中は OSC 出力を約 200ms で 0 へ戻す。再接続すると信号に追従する。
- 想定した Service / Characteristic が無い場合は再接続せず `Error` になる。GATT Discover で実際の構造を確認する。
- **Disconnect** を押すと再接続しない。

## VRChat 側の設定

1. VRChat で Action Menu → Options → OSC → **Enabled**。
2. アバターに Float パラメータ `TailEMG`(0.0–1.0)を Expression Parameters と Animator の両方に用意する。
3. 送信先の既定値: `127.0.0.1:9000` / `/avatar/parameters/TailEMG` / 30 Hz(UI で変更可。Rate は次回 Start/Connect から有効)。
4. 反応しない場合は OSC → Reset Config を試す。

## 既知の問題 / 未実装

- Scan / 接続 / 通知受信は実機で確認済み。ただし切断・自動再接続(1s → 2s → 5s…)の挙動は、実機では未確認。
- 実機のペリフェラルは通知を約 700Hz のバーストで送り、値が 2 系列に交互に並ぶ(例: 64 / 54 / 66 / 55…)。OSC は 30Hz で最新値を取るので影響は小さい。原因は未調査。
- Smoothing(α)は「33ms あたりの係数」で、入力レートに依存しない(Fake 50Hz でも BLE 700Hz でも同じ滑らかさ)。
- CSV の OSC 列は、サンプル到着時点で最後に送信した値(30Hz で更新される)。
- Calibration は 3 秒固定、チャートの時間幅(10 秒)も固定。
- 通知ログは最新 200 行を保持し、約 10Hz で更新する。止めたいときは `Pause log`。
- Windows は同一アドレスに対して Advertisement の名前が別イベントで届くことがあるため、名前が遅れて表示されることがある。
