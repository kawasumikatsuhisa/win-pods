# WinPods

Windows で AirPods を扱いやすくする、タスクトレイ常駐アプリ。
MagicPods 相当のことをゼロから作る個人プロジェクト。

- バッテリー残量 (左 / 右 / ケース) と充電状態をトレイから確認する
- ワンクリックで接続 / 切断する
- ケースを開けたらポップアップで残量を出す

Apple が AirPods の状態を BLE のアドバタイズメントで平文ブロードキャストしている
(Proximity Pairing メッセージ) ことを利用しており、GATT 接続なしに残量を取得する。
プロトコルの詳細は [`docs/proximity-pairing.md`](docs/proximity-pairing.md) を参照。

## 状態

**開発初期 (v0.1 の実装途中)。実機での動作確認はこれから。**

デコードのロジックとアプリの骨格は書けているが、Windows 実機 + AirPods での検証が
まだ済んでいない。特に以下は解析情報からの推定を含む:

- status バイトのビットの意味 (左右の判定以外)
- Lid Open Counter の bit3 の解釈
- `BluetoothSetServiceState` による接続 / 切断の実際の挙動

確認すべき項目は [`docs/roadmap.md`](docs/roadmap.md) にまとめてある。

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
  WinPods.Core/      Proximity Pairing のデコードと状態モデル (net8.0, OS 非依存)
  WinPods.Windows/   WinRT / Win32 の実装 (BLE スキャン, A2DP/HFP 接続制御)
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
  [`docs/architecture.md`](docs/architecture.md) に書いてある
  (要点: トレイ常駐との相性、WinRT の Bluetooth API は WPF からも同じように使える)。
- A2DP / HFP の接続制御は Win32 の `BluetoothSetServiceState` を使う。
  クラシック Bluetooth のプロファイルを能動的に繋ぐ公開 WinRT API が無いため。
- AirPods の BLE アドバタイズのアドレスはランダマイズされており、
  A2DP の接続に使うアドレスとは一致しない。この制約への対処は
  [`docs/architecture.md`](docs/architecture.md) の「既知の制約」を参照。

## 注意

Apple の非公開プロトコルの解析に基づいており、公式にサポートされたものではない。
将来のファームウェア更新で動かなくなる可能性がある。
AirPods は Apple Inc. の商標。このプロジェクトは Apple とは無関係。
