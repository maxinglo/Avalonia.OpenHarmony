<a id="chap-storage"></a>

# HarmonyOS 文档存储

[英文原文：`docs/storage.md`](https://github.com/maxinglo/Avalonia.OpenHarmony/blob/main/docs/storage.md) · [返回目录](../../README.zh-CN.md#contents)

Avalonia 存储提供程序使用 API 26 的 `DocumentViewPicker` 选择文件、保存位置或目录。目录选择要求设备支持对应系统能力；ArkTS 处理器在打开目录选择器前会先检查。API 26 的二合一设备支持多选目录，其他支持目录选择的设备类型使用单选。

用应用的系统请求委托创建 `OpenHarmonyStorageProvider`，将其作为顶层窗口的 `IStorageProvider` 提供给应用。可将 `canIUse('SystemCapability.FileManagement.UserFileService.FolderSelection')` 的实际结果传给可选构造参数 `canPickFolder`，使 Avalonia 能获知运行时能力。将 `storage.*` 请求路由到共享 ArkTS 模块 `StorageRequests.ets` 中的 `executeStorageRequest`。

Avalonia 文件和目录对象会保留系统 URI 及其中的百分号编码。应用打开流时，ArkTS 通过 `fileIo.open` 打开 URI；桥接层在完成回调期间复制描述符，随后 ArkTS 关闭原描述符。托管流或目录枚举器拥有副本。所选目录的描述符还用于枚举和相对路径下的目录创建。所有操作均受选择器授予的权限约束。**选中一个文件不代表应用获准访问其父目录或其他本地路径。**

选择器标题、覆盖提示和最近使用的位置由系统控制。适配器映射简单的扩展名通配模式，以及在系统 UTD 注册表中有关联后缀的 MIME 类型。保存时，每种 Avalonia 文件类型取第一个可表示的扩展名，并可通过原生索引报告所选类型。打开文件的 API 不提供所选类型索引。见[文档选择器 API](https://developer.huawei.com/consumer/en/doc/harmonyos-references/js-apis-file-picker/)。

支持 `FolderObtain` 的设备上，宿主分别取得公共目录授权后，才能直接查询桌面、文档或下载目录。没有相应授权时，查询返回 `null`；选择器有独立的选择授权，仍可使用。文件元数据包括大小及修改时间；SDK 支持时保留纳秒精度。API 26 提供的是元数据变更时间，此 API 没有等价的创建时间。见[公共目录授权](https://developer.huawei.com/consumer/en/doc/HarmonyOS-Guides/request-dir-permission/)和[文件元数据 API](https://developer.huawei.com/consumer/en/doc/harmonyos-references/js-apis-file-fs/)。

在 API 26 PC 环境中，Counter 仅指定纯文本 MIME 类型便保存了 24 字节测试文件，并取得所选保存类型。用相同的 MIME 筛选重新打开后，取得了文件和修改时间，但没有打开类型索引。该系统镜像未向 Counter 授予持久公共目录权限。

## 移动或删除所选目录的内容

对**通过选择器返回的目录**取得的子项调用 `DeleteAsync` 或 `MoveAsync`。保留选择器返回的目录对象，通过其子项 API 操作，以保留授权来源。解析从所选目录的描述符开始，逐级相对父目录定位子目录，不跟随符号链接。单独选中文件后再按 URI 查找其父目录，不能获得同样的授权来源。

移动时目标目录必须来自同一提供程序，子项保留原文件名。目标已存在则失败；文件系统无法原子移动时返回 `null`。可删除子文件或空子目录，不能递归删除，也不能删除或移动选择器返回的根目录。设备仍执行自身的文件系统权限检查。

## 启用持久书签

将 `FileAccess.Read` 或 `FileAccess.ReadWrite` 作为 `OpenHarmonyStorageProvider` 的第 3 个构造参数，指定所需的最大持久访问权限。使用两个参数的构造函数时，书签保持禁用。

接入方须在自身模块配置中声明 `ohos.permission.FILE_ACCESS_PERSIST`。按原文所述，API 26 本地开发将其作为系统授予的普通权限；上架分发须遵循华为受限权限申请流程。Counter 和可复用的宿主资源均未添加此权限。

只有设备具备 `SystemCapability.FileManagement.AppFileService.FolderAuthorization`，且调用方实际已获 `FILE_ACCESS_PERSIST` 授权，ArkTS 处理器才会报告支持书签或执行书签操作。授权方允许时，可为选择器返回的项目创建书签。不支持远程或分布式 URI、通过任意路径查得的项目，也不持久化临时创建、删除和重命名权限。

将 `SaveBookmarkAsync` 返回的不透明字符串保存到应用数据中。重新打开时，提供程序激活持久权限，并检查项目仍然存在且类型符合预期。通过只读书签重新打开的项目及其子项，在本提供程序中始终为只读。移除保存记录时，调用 `IStorageBookmarkItem.ReleaseBookmarkAsync` 撤销系统权限。

保存或重新打开操作不可用、或授权遭拒时，返回 `null`。撤销失败时保留记录，以便重试。SDK 错误码 `201` 表示应用缺少权限，`801` 表示能力不可用，`13900001` 可表示禁止持久化、路径无效或权限从未持久化等策略错误。

## 验证

API 26 已验证系统文件选择，以及 24 字节文件写入和读回。Windows 托管测试覆盖选择器请求序列化、保存类型映射、取消、转义的 Unicode 路径和描述符所有权。另一个 Windows 测试模拟系统授权，验证书签生命周期，并通过原生适配层操作授权范围内的测试文件。API 26 ArkTS 处理器可编译；SDK 对 x86_64 和 arm64 的目录结构和原生符号均完成校验。

在独立的 API 26 x86_64 书签测试中，程序保存了所选测试文件的令牌，新进程无需重新打开选择器即可读回全部 24 字节。**系统撤销调用虽返回成功，第三个进程仍能打开同一文件。** 整个测试期间，持久化策略检查始终返回 `false`；测试最后清除了应用私有令牌。授权与撤销是否真正生效，以及目录移动和删除，仍需在目标设备上验证。

参考：[选择用户文件](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/select-user-file/) · [文档选择器 API](https://developer.huawei.com/consumer/en/doc/harmonyos-references/js-apis-file-picker/) · [原生侧通过描述符访问文件](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/file-native-side/) · [持久化临时权限](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/file-persistpermission/) · [受限权限](https://developer.huawei.com/consumer/en/doc/HarmonyOS-Guides/restricted-permissions/)
