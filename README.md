# Hatch

Windows 用 hosts ファイル管理ツール。グループ・プリセットによる一括切り替えに対応。

## ダウンロード

**[最新版をダウンロード](https://github.com/vellivore/Hatch/releases/latest)**

`Hatch.exe` をダウンロードして実行するだけで使えます。インストール・.NET ランタイム不要です。
管理者権限が必要です（hosts ファイルの書き換えのため）。

## 機能

| 機能 | 説明 |
|------|------|
| エントリ管理 | hosts エントリの追加・編集・削除・有効/無効切り替え |
| 階層グループ | `/` 区切りで最大3階層（大区分/中区分/小区分） |
| プリセット | 現在の有効/無効状態をスナップショット保存・一括切り替え |
| タスクトレイ | 常駐してプリセットを右クリックで即切り替え |
| テキスト編集 | hosts ファイルを直接テキスト編集 |
| バックアップ/リストア | hosts ファイルのバックアップと復元 |
| 自動バックアップ | 書き込みのたびに直前の hosts を退避（直近 20 世代）。トレイの「直前の状態に戻す」で復元 |
| DNS フラッシュ | 切り替え時に `ipconfig /flushdns` を自動実行 |

### ショートカット

| キー | 動作 |
|------|------|
| Ctrl+S | 適用（hosts に書き出し） |
| F5 | 最新化（hosts を再読み込み） |

## 自動バックアップと書き込みの安全策

hosts への書き込み（適用・プリセット切替・テキスト編集・リストア・直前の状態に戻す）はすべて同じ手順で行います。

1. 書き込み前の hosts を `%LOCALAPPDATA%\Hatch\backups\hosts.yyyyMMdd-HHmmss.txt` に退避します。直近 20 世代を残し、古いものから削除します
2. hosts と同じフォルダの一時ファイルに書き出し、hosts と置き換えます。途中で失敗しても hosts が空になりません
3. 書き込んだ hosts を読み戻して内容を照合します。一致しなければ退避したファイルに戻し、エラーを表示します

タスクトレイの「直前の状態に戻す」は、最新の退避ファイルを hosts に書き戻して DNS キャッシュをフラッシュします。
戻す前の状態も退避されるので、もう一度実行すると戻す前に戻ります。

## スクリーンショット

ダークテーマ UI、グループ別の色タグで視認性を確保。

## 技術スタック

- C# / .NET 8.0 / WPF / MVVM (CommunityToolkit.Mvvm)
- System.Windows.Forms.NotifyIcon（タスクトレイ）
- ポータブル設定（exe 同階層の JSON）

## ビルド

```
dotnet build
```

自己完結型バイナリ:
```
dotnet publish Hatch -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/
```
