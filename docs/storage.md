# HarmonyOS document storage

The Avalonia storage provider uses the API 26 `DocumentViewPicker` for file selection, save destinations, and
folder selection. The corresponding system capability must be present to select folders. The handler checks that
capability on the ArkTS side before opening the folder picker. API 26 supports multi-folder selection on 2-in-1
devices. Other supported profiles open the single-folder picker.

Create `OpenHarmonyStorageProvider` with the application's system request delegate and expose it as the top-level
`IStorageProvider`. Pass the device's `canIUse('SystemCapability.FileManagement.UserFileService.FolderSelection')`
result as the optional `canPickFolder` constructor argument to expose the runtime capability to Avalonia.
Route `storage.*` requests to `executeStorageRequest` in the Counter sample's `StorageRequests.ets` module.

Avalonia file and folder objects preserve the system URI and its percent-encoded contents. When an app opens a
stream, ArkTS opens the URI through `fileIo.open`, and the bridge duplicates the descriptor during the completion
callback. ArkTS closes the original descriptor. The managed stream or directory enumerator owns the duplicate.
The selected folder's descriptor is also used for directory enumeration and relative directory creation.
These operations use the permissions granted to the application by the picker. A file grant does not give access
to the file's parent or to unrelated local paths.

The system controls the picker title, overwrite prompts, and recent-location preferences. The adapter maps simple
extension globs to native filters and forwards the save filename and suggested folder. Save choices use the first
representable extension for each Avalonia file type. The system may return a selected type for save. MIME-only
filters, custom picker titles, and a selected type for open are currently unavailable. Bookmarks, persistent
grants, well-known folder discovery, and moving or deleting through these storage objects are left unsupported.

The Windows managed probe exercises selection, serialization, save type mapping, cancellation, escaped Unicode
paths, and actual file reads and writes via owned handles. The ArkTS handler compiles with the API 26 SDK. The SDK
also verifies the libc directory ABI on x86_64 and arm64. A HarmonyOS run remains necessary to verify system picker
presentation, URI permissions, directory operations, and descriptor transfer end to end.

- [Selecting user files](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/select-user-file/)
- [Document picker API](https://developer.huawei.com/consumer/en/doc/harmonyos-references/js-apis-file-picker/)
- [File access via a descriptor on the native side](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/file-native-side/)
