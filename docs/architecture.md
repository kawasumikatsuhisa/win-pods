# アーキテクチャ

## 全体像

```
        BLE アドバタイズ                       Win32 / WinRT
              │                                     │
              ▼                                     ▼
  BluetoothLeAirPodsWatcher          SafeBluetoothAudioConnector
  WindowsPairedDeviceProvider        BluetoothDiagnostics
              │  (WinPods.Windows)                  │
              ▼                                     │
      IAirPodsAdvertisementWatcher ──┐              │
                                     ▼              ▼
                              AirPodsMonitor ── MainViewModel
                          (状態保持・蓋の検出)          │
                                                      ▼
                                          TrayIconHost / PopupWindow
                                                  (WinPods.App)
```

## プロジェクト構成

| プロジェクト | TFM | 役割 |
| --- | --- | --- |
| `WinPods.Core` | `net8.0` | Proximity Pairing のデコード、状態モデル、対象デバイス選択、OS 抽象化インターフェイス。**OS 非依存**なのでどこでもテストできる。 |
| `WinPods.Windows` | `net8.0-windows10.0.19041.0` | WinRT / Win32 の実装。BLE スキャン、ペアリング済みデバイスの列挙、接続状態取得、Bluetooth 診断。 |
| `WinPods.App` | `net8.0-windows10.0.19041.0` (WPF) | タスクトレイ常駐の UI。ポップアップ、コンテキストメニュー。 |
| `WinPods.Core.Tests` | `net8.0` | `WinPods.Core` の単体テスト。 |

デコードや対象デバイス選択のロジックを OS 非依存の `WinPods.Core` に置き、
Windows 実機がなくても壊れやすい判断ロジックをテストできるようにしている。

## 技術選定の理由

### WPF を選んだ理由

WinUI 3 のほうが見た目は現代的だが、このアプリはウィンドウを持たず
タスクトレイに常駐するのが主用途で、WPF のほうが取り回しがよい。

WinRT の Bluetooth API は Windows 向け TFM を指定すれば WPF からも呼べるため、
Bluetooth のためだけに WinUI 3 を採用する必要はない。

## BLE バッテリー監視

`BluetoothLeAirPodsWatcher` が `BluetoothLEAdvertisementWatcher` を使って
Apple Company ID (`0x004C`) の Manufacturer Data を監視する。
`ProximityPairingParser` が Continuity Proximity Pairing (`0x07 0x19`) をデコードし、
左右・ケースの残量と充電フラグを `AirPodsMonitor` へ渡す。

BLE コールバックは WinRT のスレッドプールから発火するため、`AirPodsMonitor` は
内部状態をロックで守り、UI 反映は `Dispatcher.BeginInvoke` を経由する。

## 対象デバイスの選択

BLE アドバタイズのランダムアドレスと、Classic Bluetooth でペアリングされた
デバイスのアドレスは一致しない。このため BLE パケットだけから
「どのペアリング済み AirPods か」を厳密に対応づけることはできない。

一方、以前の実装のように `WindowsPairedDeviceProvider` が返す先頭の機器を
そのまま採用するのも安全ではない。OS の列挙順はユーザーの優先順位ではなく、
Bluetooth スピーカーなど別のオーディオ機器を操作する可能性がある。

`PairedDeviceSelector` は次の順で対象を決める。

1. 名前から AirPods / Beats と判断できる機器を優先し、その中では接続中を優先
2. 名前で判断できない場合、接続中の機器が 1 台だけなら採用
3. オーディオ機器自体が 1 台だけなら採用
4. 複数候補から安全に決められない場合は null とし、操作しない

将来はユーザーが対象デバイスを明示選択し、Bluetooth の安定した識別子を保存する
設定画面を追加するのが望ましい。

## Classic Bluetooth の接続制御

### `BluetoothSetServiceState` を接続トグルに使わない

Windows の `BluetoothSetServiceState` は、名前から想像しやすい
「一時的に A2DP を接続 / 切断する API」ではない。

Microsoft の仕様では、サービスを ENABLE にすると対応するデバイスドライバーを
インストールし、DISABLE にすると削除する操作である。

実機でも Windows 11 build 26200 + AirPods Pro で DISABLE 後に A2DP / HFP の
登録が消え、Windows が AirPods をオーディオ機器として扱えなくなり、
再ペアリングが必要になる事象を確認した。

このため現在の実行経路から `BluetoothSetServiceState` と、それを使っていた
`BluetoothServiceStateConnector` は削除した。

### 現在の `SafeBluetoothAudioConnector`

Windows の公開 API には、任意の Classic Bluetooth A2DP Sink へ
確実に「今すぐ接続」「安全に一時切断」する API がない。

現在は次の方針とする。

- 接続済みかどうかは `BluetoothGetDeviceInfo` の `fConnected` で確認する
- 未接続時は `BluetoothDevice.FromBluetoothAddressAsync` でデバイスを開き、
  `GetRfcommServicesAsync(BluetoothCacheMode.Uncached)` を実行して Windows の
  自動接続を best-effort で促す
- その後、実際の `fConnected` が変化した場合だけ成功とする
- 切断は危険な代替手段を使わず、未サポートとして返す
- UI から Windows の `ms-settings:bluetooth` を開けるようにする

この方式は「必ずワンクリックで接続できる」ものではないが、デバイス構成を壊さない。
完全な接続トグルを実現する場合は、将来の Windows 公開 API か、十分に検証した別の
仕組みが必要。

## 診断

`BluetoothDiagnostics` は読み取り専用で、次をレポートする。

- OS バージョン
- Bluetooth ラジオ数
- `BluetoothGetDeviceInfo` の結果
- 接続 / 登録 / 認証状態
- Class of Device
- `BluetoothEnumerateInstalledServices` が返すサービス GUID 一覧

トレイメニューの「診断情報をコピー」から取得できる。

## 既知の制約

- **BLE アドレスと Classic Bluetooth アドレスが一致しない。** 複数セットを
  厳密に識別するには設定で対象を固定する仕組みが必要。
- **Classic Bluetooth の確実な接続 / 切断は未解決。** 現在は安全性を優先している。
- バッテリー残量は 10% 刻み。プロトコル上それ以上の分解能は無い。
- ケースの開閉検出は Lid Open Counter の変化に依存しており、
  アドバタイズを取りこぼすと検出できない。

## 終了処理

`BluetoothLeAirPodsWatcher.Dispose()` は WinRT watcher を停止してからイベント購読を解除する。
以前は disposed フラグを先に立てていたため `Stop()` が早期 return し、スキャン停止処理が
実行されない問題があった。
