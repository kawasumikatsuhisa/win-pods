# WinPods

Windows で AirPods を扱いやすくする、タスクトレイ常駐アプリ。
MagicPods 相当の体験をゼロから検証している個人プロジェクト。

- バッテリー残量 (左 / 右 / ケース) と充電状態をトレイから確認する
- ケースを開けたらポップアップで残量を出す
- Windows が認識している接続状態を追跡する
- 接続を安全な範囲で試し、必要なら Windows の Bluetooth 設定をすぐ開く

Apple が AirPods の状態を BLE のアドバタイズメントで平文ブロードキャストしている
(Proximity Pairing メッセージ) ことを利用しており、GATT 接続なしに残量を取得する。
プロトコルの詳細は [`docs/proximity-pairing.md`](docs/proximity-pairing.md) を参照。

## 状態

**開発初期 (v0.1)。実機検証を継続中。**

デコードのロジックとアプリの骨格は実装済みだが、Windows / AirPods の組み合わせに
依存する挙動はまだ検証が必要。特に以下は解析情報や実測に基づく:

- status バイトのビットの意味 (左右の判定以外)
- Lid Open Counter の bit3 の解釈
- Windows から Classic Bluetooth の A2DP 接続を開始する方法

確認すべき項目は [`docs/roadmap.md`](docs/roadmap.md) にまとめてある。

> **重要**: `BluetoothSetServiceState` は一時的な Bluetooth 接続 / 切断 API ではない。
> サービスを無効化すると対応するドライバー構成が外れ、環境によっては再ペアリングが
> 必要になることが実機で確認できた。そのため現在の実行経路では
> `BluetoothSetServiceState` を使用しない。詳細は
> [`docs/architecture.md`](docs/architecture.md) を参照。

## 接続 / 切断について

Windows の公開 API には、任意の Classic Bluetooth A2DP Sink に対して
「今すぐ接続」「安全に一時切断」を確実に行う API がない。

現在の WinPods は次の方針にしている:

- **接続**: デバイス構成を変更しない SDP 問い合わせで Windows の自動接続を促し、
  実際に接続されたかを確認する。接続されなければ失敗として表示する
- **切断**: ドライバー構成を変更する危険な代替手段は使わない。Windows の
  Bluetooth 設定から切断する
- トレイメニューから **「Bluetooth 設定を開く」** を選べる

つまり、バッテリー監視とポップアップは独立して動作するが、完全なワンクリック接続は
今後も Windows 側の安全な手段を調査する必要がある。

## 対象デバイスの選択

ペアリング済みオーディオ機器の OS 列挙順は優先順位ではないため、先頭の機器を
無条件に操作しない。

現在は次の順で対象を選ぶ:

1. 名前から AirPods / Beats と判断できる機器 (接続中を優先)
2. 名前を変更している場合、接続中のオーディオ機器が 1 台だけならその機器
3. ペアリング済みオーディオ機器自体が 1 台だけならその機器
4. 複数候補から安全に決められない場合は操作しない

将来は設定画面で対象デバイスを明示選択できるようにする予定。

## 動作環境

- Windows 10 バージョン 1809 (build 17763) 以降
- 実行: .NET 8 以降のデスクトップランタイム
- ビルド: .NET SDK 8.0.100 以降 (9 / 10 でも可。`global.json` は下限のみ指定している)
- Bluetooth LE に対応したアダプタ
- AirPods が **Windows 側でペアリング済み** であること
  (ペアリング自体は Windows の設定アプリで行う)

## ビルド

```powershell
dotnet restore
dotnet build -c Release
dotnet test
dotnet run --project src/WinPods.App
```

配布用の単一 exe:

```powershell
dotnet publish src/WinPods.App -c Release -r win-x64 `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

`WinPods.Core` と `WinPods.Core.Tests` は OS 非依存 (`net8.0`) なので、
Windows 以外でもビルド・テストできる:

```bash
dotnet test tests/WinPods.Core.Tests
```

## プロジェクト構成

```
src/
  WinPods.Core/      Proximity Pairing のデコード、状態モデル、対象デバイス選択 (net8.0)
  WinPods.Windows/   WinRT / Win32 の実装 (BLE スキャン、接続状態取得、診断)
  WinPods.App/       WPF のトレイ常駐 UI
tests/
  WinPods.Core.Tests/
docs/
  proximity-pairing.md   BLE メッセージのバイト配置
  architecture.md        構成と技術選定の理由
  roadmap.md             実装状況と今後の予定
tools/
  generate_icon.py       アプリ / トレイアイコンの生成
```

## 技術メモ

- UI は **.NET 8 + WPF**。WinUI 3 ではなく WPF を選んだ理由は
  [`docs/architecture.md`](docs/architecture.md) に記載している。
- AirPods の BLE アドバタイズのアドレスはランダマイズされており、
  Classic Bluetooth の接続に使うアドレスとは一致しない。
- `BluetoothSetServiceState` は接続トグル用途には使わない。

## 注意

Apple の非公開プロトコルの解析に基づいており、公式にサポートされたものではない。
将来のファームウェア更新で動かなくなる可能性がある。
AirPods は Apple Inc. の商標。このプロジェクトは Apple とは無関係。
