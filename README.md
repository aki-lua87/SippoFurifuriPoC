# TailEMG PoC

MyoWare 2 の筋電値を VRChat の Avatar Parameter (OSC) へ送る PoC。仕様は依頼書.txt(リポジトリには含めずローカル管理)。

## 現在の進捗

| Milestone | 内容 | 状態 |
|---|---|---|
| A | WPF / FakeEmgInput / Signal Processing / OSC | **完了**(VRChat 疎通確認済み) |
| B | BLE Device Discovery / Diagnostic UI | **実装済み**(実機確認待ち) |
| C | MyoWareBleInput(Notification 受信・parse・Pipeline 接続・簡易 Reconnect) | **実装済み**(実機確認待ち) |
| D | Calibration 操作 / CSV Logging / Chart / Settings 保存 | 未着手 |

## 構成

```
TailEmg.sln
src/TailEmg.Core      Input / Processing / Osc / Ble(モデル・UUID・payload parser・GATT レポート)  Windows API 非依存
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
3. REST / MAX は現状手入力(既定 0 / 4095)。Deadzone(0.05) / Curve / Gamma(1.0) / Smoothing α(0.25) はリアルタイム反映。
4. **Stop** またはウィンドウを閉じると約 200ms で 0 へ戻し、最後に `0.0` を 1 回送信する。

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

- BLE 実装は実機未検証(開発 PC に Bluetooth 無線がなく、スキャン・接続は動作確認できていない)。
- Calibration の REST / MAX 取得ボタン、CSV ログ、Chart、設定保存は Milestone D。
- 通知ログは最新 200 行を保持し、約 10Hz で更新する。止めたいときは `Pause log`。
- Windows は同一アドレスに対して Advertisement の名前が別イベントで届くことがあるため、名前が遅れて表示されることがある。
