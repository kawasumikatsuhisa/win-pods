# アーキテクチャ

## 全体像

```
        BLE アドバタイズ                     Win32 / WinRT
              │                                   │
              ▼                                   ▼
  BluetoothLeAirPodsWatcher        BluetoothServiceStateConnector
  WindowsPairedDeviceProvider      (A2DP / HFP の接続・切断)
              │  (WinPods.Windows)                │
              ▼                                   │
      IAirPodsAdvertisementWatcher ──┐            │
                                     ▼            ▼
                              AirPodsMonitor ── MainViewModel
                          (状態保持・蓋の検出)        │
                                                    ▼
                                        TrayIconHost / PopupWindow
                                                (WinPods.App)
```

## プロジェクト構成

| プロジェクト | TFM | 役割 |
| --- | --- | --- |
| `WinPods.Core` | `net8.0` | Proximity Pairing のデコード、状態モデル、OS 抽象化インターフェイス。**OS 非依存**なのでどこでもテストできる。 |
| `WinPods.Windows` | `net8.0-windows10.0.19041.0` | WinRT / Win32 の実装。BLE スキャン、ペアリング済みデバイスの列挙、A2DP/HFP の接続制御。 |
| `WinPods.App` | `net8.0-windows10.0.19041.0` (WPF) | タスクトレイ常駐の UI。ポップアップ、コンテキストメニュー。 |
| `WinPods.Core.Tests` | `net8.0` | `WinPods.Core` の単体テスト。 |

デコードのロジックを OS 非依存の `WinPods.Core` に閉じ込めているのが要点で、
プロトコル解析のような一番壊れやすい部分をテストで固められるようにしている。

## 技術選定の理由

### WPF を選んだ理由

WinUI 3 のほうが見た目は現代的だが、このアプリはウィンドウを持たず
タスクトレイに常駐するのが主用途で、WinUI 3 はそこが弱い
(NotifyIcon 相当が標準にない、非パッケージ実行の取り回しが面倒)。

WinRT の Bluetooth API は TFM を `net8.0-windows10.0.19041.0` にすれば
WPF からもそのまま呼べるため、WinUI 3 を選ぶ動機は薄い。

### 接続制御に Win32 API を使う理由

クラシック Bluetooth のオーディオプロファイル (A2DP / HFP) を能動的に
接続・切断する公開 WinRT API は存在しない。Win32 の
`BluetoothSetServiceState` にサービスクラス GUID を渡す方法を採る。

- A2DP Sink: `{0000110B-0000-1000-8000-00805F9B34FB}`
- Hands-Free: `{0000111E-0000-1000-8000-00805F9B34FB}`

#### 危険: BLUETOOTH_SERVICE_DISABLE は「切断」ではない

`BluetoothSetServiceState` に `BLUETOOTH_SERVICE_DISABLE` を渡す操作は、
一時的な切断ではなく **そのデバイスからオーディオプロファイルの登録を外す**。
`BluetoothEnumerateInstalledServices` の一覧から当該 GUID が消える。

通常は `BLUETOOTH_SERVICE_ENABLE` で戻せるが、後述のとおり ENABLE が
`ERROR_INVALID_PARAMETER(87)` を返す環境では**戻せない**。実際に
Windows 11 build 26200 + AirPods Pro で、切断操作によって `110b` (A2DP Sink) と
`111e` (HFP) が登録から消え、Windows が AirPods をオーディオ機器として
扱えなくなる事象を発生させた。復旧にはデバイスの削除と再ペアリングが必要だった。

このため切断処理は、**先に ENABLE を実行して成功することを確認できた場合のみ**
DISABLE を実行する。ENABLE が失敗する環境では切断機能を提供しない。

#### 実測: BluetoothSetServiceState が 87 を返す環境がある

Windows 11 build 26200 + AirPods Pro で、`BluetoothSetServiceState` が
A2DP / HFP どちらも `ERROR_INVALID_PARAMETER(87)` を返す事例を確認している。
このとき、

- `BluetoothGetDeviceInfo` は成功する (構造体サイズ 560、登録済み・認証済みとも True)
- `BluetoothEnumerateInstalledServices` にも `110b` (A2DP Sink) と
  `111e` (HFP) の両方が含まれている

つまり GUID もアドレスも構造体レイアウトも正しいのに拒否される。原因は未特定。

このため接続処理は単発では諦めず、次の順で試して最初に実際に接続できたものを採る:

1. ラジオハンドルを指定して `BluetoothSetServiceState(ENABLE)`
2. `hRadio` に NULL を渡して同じ操作
3. WinRT の `GetRfcommServicesAsync(Uncached)` で SDP 問い合わせを行い、
   ACL リンクを張らせてオーディオドライバの自動接続を促す

どの手法がどう失敗したかは UI のステータス行に残す。トレイメニューの
「診断情報をコピー」で、OS が認識しているサービス一覧を確認できる。

## 既知の制約

- **BLE アドレスとクラシック Bluetooth アドレスが一致しない。**
  アドバタイズ元とペアリング済みデバイスを厳密に対応づけられないため、
  v0.1 は「最も電波が強い Proximity Pairing 送信元 = ユーザーの AirPods」
  「ペアリング済みオーディオデバイスの先頭 = 接続対象」という近似で動く。
  複数のセットを使い分ける場合は破綻するので、設定でデバイスを選べるようにする必要がある。
- バッテリー残量は 10% 刻み。プロトコル上それ以上の分解能は無い。
- ケースの開閉検出は Lid Open Counter の変化に依存しており、
  アドバタイズを取りこぼすと検出できない。

## スレッド

`BluetoothLEAdvertisementWatcher.Received` は WinRT のスレッドプールから発火する。
`AirPodsMonitor` はロックで内部状態を守り、UI への反映は
`MainViewModel` / `TrayIconHost` 側で `Dispatcher.BeginInvoke` を通して行う。
