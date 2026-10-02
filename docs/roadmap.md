# ロードマップ

## v0.1 (いま作っているもの)

- [x] Proximity Pairing メッセージのデコード (バッテリー / 充電状態 / 機種判別)
- [x] Lid Open Counter によるケース開閉の検出
- [x] BLE アドバタイズメントの監視 (`BluetoothLEAdvertisementWatcher`)
- [x] タスクトレイ常駐 + バッテリー表示ポップアップ
- [x] ケースを開けたときのポップアップ通知
- [x] ペアリング済み機器から AirPods / Beats を安全に選ぶロジック
- [x] Windows が認識している接続状態の監視
- [x] Windows の Bluetooth 設定をアプリから開く
- [ ] **安全で確実な A2DP 接続 / 切断手段**
- [ ] **実機での検証** (下記「実機で確かめること」)

### 実機で確かめること

これらはドキュメントと OSS 実装から組み立てた推定を含むため、
実機 (Windows PC + AirPods) での確認が必要:

1. Proximity Pairing メッセージを実際に受信できるか、27 バイトで届くか
2. status バイト bit5 の左右判定が実際の左右と合っているか
3. 充電フラグの bit0 / bit1 の対応が合っているか
4. Lid Open Counter の bit3 が「開 = 0」で合っているか
5. `SafeBluetoothAudioConnector` の SDP 問い合わせで、どの Windows / AirPods の
   組み合わせなら自動接続が誘発されるか
6. AirPods 以外の Bluetooth オーディオ機器が複数ペアリングされている環境で、
   誤ったデバイスを対象にしないか

### 接続 / 切断について分かったこと

`BluetoothSetServiceState` は接続トグル用途には使えない。

Windows 11 build 26200 + AirPods Pro では ENABLE が
`ERROR_INVALID_PARAMETER(87)` を返した。また DISABLE は一時切断ではなく
プロファイルの登録を外し、A2DP / HFP が消えて再ペアリングが必要になる事象を
実機で確認した。

そのため現在の実行経路から `BluetoothSetServiceState` は削除済み。
接続は非破壊な best-effort の処理だけを行い、切断は Windows の Bluetooth 設定に
任せる。将来、安全な公開 API または十分に検証できる手段が見つかった場合のみ
ワンクリック接続 / 切断を復活させる。

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
