# TailEMG PoC

MyoWare 2 の筋電値を VRChat の Avatar Parameter (OSC) へ送る PoC。仕様は依頼書.txt(リポジトリには含めずローカル管理)。

## 現在の進捗: Milestone A まで

| Milestone | 内容 | 状態 |
|---|---|---|
| A | WPF / FakeEmgInput / Signal Processing / OSC | **完了** |
| B | BLE Device Discovery / Diagnostic UI | 未着手(実機待ち) |
| C | MyoWareBleInput | 未着手 |
| D | Calibration / Logging / Chart / Settings / Reconnect | 未着手 |

Fake → OSC → VRChat の疎通確認後に B 以降へ進む。

## 構成

```
TailEmg.sln
src/TailEmg.Core   Input(IEmgInput, FakeEmgInput) / Processing / Osc  (Windows API 非依存)
src/TailEmg.App    WPF + MVVM (TFM: net8.0-windows10.0.19041.0)
tests/TailEmg.Core.Tests   xUnit
```

`TailEmg.Windows`(BLE 実装)は Milestone B で追加する。NuGet は xUnit 系のみ(テンプレート標準)。OSC は依存なしの自前実装(`,f` のみ)。

処理経路: `IEmgInput → EmgPipeline(Normalize → Deadzone → Curve → EMA) → OscPublisher(30Hz で最新値を送信)`。
入力周期と OSC 送信周期は分離済み。`Channel` 概念は `EmgSample` / `EmgPipeline` に残してある。

## 必要環境

- Windows 10/11
- .NET 8 SDK

## 起動

```powershell
dotnet run --project src/TailEmg.App
dotnet test
```

## Fake Input でのテスト

1. アプリを起動し、Fake Input の Mode を選ぶ。
   - Manual: スライダー (0–4095)
   - Sine: 0→4095→0 を指定周期(既定 4 秒)で繰り返す
2. **Start** を押す。Output の Normalized / Filtered / OSC が動く。
3. REST / MAX は現状手入力(既定 0 / 4095)。Deadzone(0.05) / Curve / Gamma(1.0) / Smoothing α(0.25) はリアルタイムに反映される。
4. **Stop** またはウィンドウを閉じると、約 200ms で 0 へ戻し、最後に `0.0` を 1 回送信する。

## VRChat 側の設定

1. VRChat で Action Menu → Options → OSC → **Enabled**。
2. アバターに Float パラメータ `TailEMG`(0.0–1.0、Animator と Expression Parameters 両方)を用意し、尻尾のアニメーションに接続する。
3. 送信先の既定値: `127.0.0.1:9000` / `/avatar/parameters/TailEMG` / 30 Hz(UI で変更可。Rate は次回 Start から有効)。
4. OSC 設定を変えた後に反応しない場合は VRChat の OSC → Reset Config を試す。

VRChat なしで確認したい場合は、OSC 受信ツール(例: Protokol)でポート 9000 を見る。

## 既知の問題 / 未実装

- BLE、診断、キャリブレーション操作、CSV ログ、チャート、設定保存、Reconnect は Milestone B 以降。
- OSC Host / Port / Address 変更は即時反映、Rate のみ次回 Start。
- 実際の VRChat での動作確認は未実施(受信側はループバック UDP のテストで検証)。
