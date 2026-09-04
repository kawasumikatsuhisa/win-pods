# ロードマップ

## v0.1 (いま作っているもの)

- [x] Proximity Pairing メッセージのデコード (バッテリー / 充電状態 / 機種判別)
- [x] Lid Open Counter によるケース開閉の検出
- [x] BLE アドバタイズメントの監視 (`BluetoothLEAdvertisementWatcher`)
- [x] A2DP / HFP の接続・切断 (`BluetoothSetServiceState`)
- [x] タスクトレイ常駐 + バッテリー表示ポップアップ
- [x] ケースを開けたときのポップアップ通知
- [ ] **実機での検証** (下記「実機で確かめること」)

### 実機で確かめること

これらはドキュメントと OSS 実装から組み立てた推定を含むため、
実機 (Windows PC + AirPods) での確認が必要:

1. Proximity Pairing メッセージを実際に受信できるか、27 バイトで届くか
2. status バイト bit5 の左右判定が実際の左右と合っているか
3. 充電フラグの bit0 / bit1 の対応が合っているか
4. Lid Open Counter の bit3 が「開 = 0」で合っているか
5. `BluetoothSetServiceState` で AirPods の接続・切断ができるか
   → **実測: できない環境がある。** Windows 11 build 26200 では ENABLE が
   `ERROR_INVALID_PARAMETER(87)` を返す。GUID・アドレス・構造体レイアウトは
   すべて正しく、インストール済みサービスにも `110b` / `111e` が含まれていた。
   さらに DISABLE はプロファイルの登録自体を外してしまい、ENABLE で戻せない
   環境では再ペアリングが必要になる。**代替の接続手段の調査が最優先課題。**

## v0.2

- 装着検出 (in-ear) → 外したら一時停止 / 着けたら再生
- status バイトの残りのビットの解析
- 対象デバイスを選ぶ設定画面
- Windows スタートアップへの登録

## v0.3 以降

- ノイズキャンセリングのモード切替 (AAP プロトコルの調査が必要)
- システムテーマ (ライト / ダーク) への追従、Mica 適用
- 複数デバイスの同時管理
- 配布形態の整備 (単一 exe / インストーラ)
