# Bitz3DGeo

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Bitz3DGeoは、BitzLabsエコシステムの3D対応に向けた、将来の拡張のための専門ライブラリです。CSG (Constructive Solid Geometry) とBREP (Boundary Representation) のハイブリッドモデルを目指します。

**注意: このリポジトリは現在、概念設計段階にあり、アクティブな開発は行われていません。**
BitzLabsの将来のビジョンを示すためのプレースホルダーです。

## 主な機能 (構想)

-   **`Geo3DAST`の定義**: 3D形状を表現するための、ハイブリッドなAST。
    -   **CSG (Constructive Solid Geometry)**: ブーリアン演算による形状構築。
    -   **BREP (Boundary Representation)**: 面・辺・頂点による厳密なトポロジー表現。
    -   **Mesh**: リアルタイムレンダリングや3Dプリンティングで広く使われる、ポリゴンメッシュ表現。
-   **パーサー**: STEP, IGES (BREP), STL, OBJ (Mesh) などの標準的な3Dフォーマットを解釈します。
-   **ジオメトリエンジン**: ブーリアン演算や、**テッセレーション**（BREP/CSGからMeshへの変換）などの形状操作を行います。
-   **レンダラー/コンバーター**: `Geo3DAST`を、Webで表示可能な形式（glTFなど）や、レイトレーシング用のデータに変換します。

## ✅ 初期開発ToDoリスト (概念設計のみ)

1.  **`Geo3DAST`の型定義 (構想)**:
    *   `IGeo3DNode`インターフェースを定義。
    *   `BooleanOperationNode`, `TransformNode`, `PrimitiveNode` (CSG)
    *   `BrepSolidNode` (BREP)
    *   `MeshNode` (`Vertices`, `Indices`, `Normals`, `UVs`)
2.  **ジオメトリエンジンの主要機能 (構想)**:
    *   `Tessellator`: BREP/CSGから`MeshNode`を生成する機能のインターフェースを設計。
3.  **プロジェクトファイルの作成**:
    *   空のC#クラスライブラリプロジェクト (`Bitz3DGeo.csproj`) と、構想をまとめたドキュメント（`docs/ast-design.md`）を作成。

## このライブラリの位置づけ

将来的に、BitzLabsを2Dと3Dが統合された完全なテクニカルドキュメンテーション・プラットフォームへと進化させるための基盤です。`BitzDoc`と連携し、ドキュメント内にインタラクティブな3Dモデルを埋め込めるようにすることを目指します。

### 依存関係 (予定)

-   `BitzAstCore`
-   `BitzParser`
