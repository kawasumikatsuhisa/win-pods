# Apple Proximity Pairing メッセージ

AirPods は接続中かどうかに関わらず、BLE のアドバタイズメントで自身の状態
(バッテリー残量・充電状態・ケースの開閉カウンタなど) を平文でブロードキャストしている。
WinPods はこれを受信してデコードすることで、GATT 接続なしに残量を表示する。

## 受信条件

| 項目 | 値 |
| --- | --- |
| 種別 | BLE アドバタイズメントの Manufacturer Specific Data |
| Company Identifier | `0x004C` (Apple, Inc.) |
| Continuity メッセージ種別 | `0x07` (Proximity Pairing) |
| 長さ | `0x19` (25) → type/length を含めて 27 バイト |

Windows では `Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementWatcher` で
受信する。`BluetoothLEManufacturerData.Data` の中身には Company ID は含まれず、
先頭バイトがいきなり `0x07` になる。

## バイト配置

Manufacturer Data の先頭からのオフセット。

| Offset | 内容 | WinPods での扱い |
| --- | --- | --- |
| 0 | メッセージ種別 `0x07` | 検証のみ |
| 1 | 長さ `0x19` | 検証のみ |
| 2 | prefix (`0x01` が多い) | 未使用 |
| 3-4 | Device Model ID (big-endian) | `AirPodsModel` へ変換 |
| 5 | status フラグ | bit5 のみ使用 (下記) |
| 6 | イヤホンのバッテリー (上位/下位ニブル) | 左右のバッテリー |
| 7 | 上位ニブル = 充電フラグ / 下位ニブル = ケースのバッテリー | 充電状態・ケース残量 |
| 8 | Lid Open Counter | ケースの開閉検出 |
| 9 | Device Color | 未使用 (生値のみ保持) |
| 10 | `0x00` | 未使用 |
| 11-26 | 暗号化ペイロード (16 バイト) | デコード不可 |

### Device Model ID (offset 3-4)

| ID | 機種 |
| --- | --- |
| `0x0220` | AirPods (第1世代) |
| `0x0F20` | AirPods (第2世代) |
| `0x1320` | AirPods (第3世代) |
| `0x0E20` | AirPods Pro |
| `0x1420` | AirPods Pro (第2世代) |
| `0x0A20` | AirPods Max |
| `0x0B20` | Powerbeats Pro |
| `0x0320` | Powerbeats3 |
| `0x0520` | BeatsX |
| `0x0620` | Beats Solo3 |
| `0x0C20` | Beats Solo Pro |
| `0x1720` | Beats Fit Pro |

未知の ID でも `RawModelId` は保持するので、実機で当たった値をここに追記していく。

### status バイト (offset 5)

いま確実に使っているのは **bit5 (`0x20`)** だけ。

- bit5 = 1 … offset 6 の**上位**ニブルが右、下位ニブルが左
- bit5 = 0 … offset 6 の**上位**ニブルが左、下位ニブルが右

いわゆる「プライマリ (親機) がどちら側か」を表しており、左右が入れ替わると
バッテリーニブルと充電フラグの並び順も入れ替わる。

その他のビットには装着検出 (in-ear) やケース内かどうかの情報が入っているとされるが、
**実機で裏取りできていない**ため v0.1 では解釈しない。生値は
`ProximityPairingMessage.RawStatus` に保持しているので、v0.2 の装着検出はここから始める。

### バッテリーのニブル

各ニブルは 0-10 が 0%-100% (10% 刻み)、`0x0F` は「値なし」を意味する。
つまり 1% 単位の残量はこのメッセージからは取得できない。

### 充電フラグ (offset 7 の上位ニブル)

| bit | 意味 |
| --- | --- |
| bit0 (`0x01`) | offset 6 の**下位**ニブル側のイヤホンが充電中 |
| bit1 (`0x02`) | offset 6 の**上位**ニブル側のイヤホンが充電中 |
| bit2 (`0x04`) | ケースが充電中 |

左右どちらに対応するかは status の bit5 次第なので、バッテリーニブルと同じ規則で入れ替える。

### Lid Open Counter (offset 8)

- 下位 3bit … ケースの蓋を開けた回数のカウンタ。開けるたびに増える。
- bit3 (`0x08`) … 蓋の開閉状態を表すと考えられる (0 = 開)。**要検証**。

WinPods の `LidStateTracker` は、確実性の高い「カウンタの変化」を主に使い、
カウンタが変わらず bit3 だけ変化した場合の補助的な判定として bit3 を見る。
実機で bit3 の意味が確定したら、この方針を見直すこと。

## 分かっていないこと / 今後の課題

- **BLE アドレスはランダマイズされる。** アドバタイズの送信元アドレスは、
  A2DP/HFP の接続に使うクラシック Bluetooth アドレスとは一致せず、定期的に変わる。
  「このアドバタイズはどのペアリング済みデバイスのものか」を厳密に突き合わせる方法は、
  暗号化ペイロード (offset 11-26) を復号できない限り無い。
  v0.1 は「電波が最も強い Proximity Pairing の送信元をユーザーの AirPods とみなす」
  という近似で運用する。
- status バイトの装着検出ビットの意味。
- ノイズキャンセリングのモード情報がこのメッセージに含まれるかどうか
  (含まれない場合、モード切替には別プロトコル (AAP) が必要)。

## 参考

このバイト配置は各所の解析記事・OSS 実装 (OpenPods / AirStatus / furiousMAC の
Continuity プロトコル解析など) で共通して説明されているものを基にしている。
Apple の公式ドキュメントは存在しないため、**すべて実機での検証が前提**である。
