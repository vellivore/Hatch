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
| DNS フラッシュ | 切り替え時に `ipconfig /flushdns` を自動実行 |

### ショートカット

| キー | 動作 |
|------|------|
| Ctrl+S | 適用（hosts に書き出し） |
| F5 | 最新化（hosts を再読み込み） |

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
