# HarmonyOS document storage

The Avalonia storage provider uses the API 26 `DocumentViewPicker` for file selection, save destinations, and
folder selection. The corresponding system capability must be present to select folders. The handler checks that
capability on the ArkTS side before opening the folder picker. API 26 supports multi-folder selection on 2-in-1
devices. Other supported profiles open the single-folder picker.

Create `OpenHarmonyStorageProvider` with the application's system request delegate and expose it as the top-level
`IStorageProvider`. Pass the device's `canIUse('SystemCapability.FileManagement.UserFileService.FolderSelection')`
result as the optional `canPickFolder` constructor argument to expose the runtime capability to Avalonia.
Route `storage.*` requests to `executeStorageRequest` in the shared ArkTS `StorageRequests.ets` module.

Avalonia file and folder objects preserve the system URI and its percent-encoded contents. When an app opens a
stream, ArkTS opens the URI through `fileIo.open`, and the bridge duplicates the descriptor during the completion
callback. ArkTS closes the original descriptor. The managed stream or directory enumerator owns the duplicate.
The selected folder's descriptor is also used for directory enumeration and relative directory creation.
These operations use the permissions granted to the application by the picker. A file grant does not give access
to the file's parent or to unrelated local paths.

The system controls the picker title, overwrite prompts, and recent-location preferences. The adapter maps simple
extension globs to native filters and forwards the save filename and suggested folder. Save choices use the first
representable extension for each Avalonia file type. The system may return a selected type for save. MIME-only
filters, custom picker titles, a selected type for open, and well-known folder discovery are currently unavailable.

## Moving and deleting selected directory contents

Call `DeleteAsync` or `MoveAsync` on a child item obtained through a folder returned by the system picker.
Keep the folder objects returned by that picker and use their child APIs to preserve the directory grant. These
operations start from the selected directory's descriptor and resolve each child directory relative to its
parent with symlink following disabled. A directory obtained by looking up the URI of an individually selected
file's parent lacks this provenance.

Moving uses the unchanged basename inside a destination directory from the same provider. It fails on an
existing destination and returns null when the filesystem cannot do an atomic move. File deletion and
empty-directory deletion are supported for children. Recursive deletion and deleting or relocating the
picker-selected root directory are unavailable. The device still enforces its filesystem permissions.

## Opting in to persistent bookmarks

Pass `FileAccess.Read` or `FileAccess.ReadWrite` as the third `OpenHarmonyStorageProvider` constructor argument
to select the maximum durable access the app needs. The two-argument constructor keeps bookmarks disabled.
The consuming app declares `ohos.permission.FILE_ACCESS_PERSIST` in its own module profile. API 26 treats it
as a normal, system-granted permission for local development. Market distribution follows Huawei's restricted
permission application process. No permission is added to Counter or to the reusable host assets.

The ArkTS handler requires `SystemCapability.FileManagement.AppFileService.FolderAuthorization` and an actual
grant of `FILE_ACCESS_PERSIST` to the calling app before advertising bookmarks or performing bookmark operations.
Items returned by the picker can be bookmarked when the grantor permits it. Remote and distributed URIs, items
looked up from arbitrary paths, and temporary create, delete, and rename rights are outside this bookmark
implementation.

Store the opaque string returned by `SaveBookmarkAsync` in your application's data. The provider activates the
persisted permission on reopen and verifies that the item still exists with the expected type. An item reopened
under a read-only bookmark and its children stay read-only through this provider. Call
`IStorageBookmarkItem.ReleaseBookmarkAsync` to revoke the system permission when removing the saved entry.
Unavailable or denied saves and reopens return null. A revocation failure preserves the stored record so the
caller can retry. SDK code 201 means the app permission is absent, 801 means the capability is unavailable, and
13900001 can carry a policy error such as persistence forbidden, invalid path, or permission never persisted.

## Verification

System file selection and a 24-byte file write/read have been verified on API 26. The Windows managed probe
checks picker serialization, save type mapping, cancellation, escaped Unicode paths, and file descriptor
ownership. A separate Windows probe exercises bookmark lifecycle against a simulated OS grant and performs
scoped file operations on test fixtures through a native shim. The API 26 ArkTS handler compiles, and the SDK
validates the directory structure and native symbols for x86_64 and arm64. Device verification of persistent
permissions, move, and delete still requires an opted-in system test.

- [Selecting user files](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/select-user-file/)
- [Document picker API](https://developer.huawei.com/consumer/en/doc/harmonyos-references/js-apis-file-picker/)
- [File access via a descriptor on the native side](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/file-native-side/)
- [Persisting temporary permissions](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/file-persistpermission/)
- [HarmonyOS restricted permissions](https://developer.huawei.com/consumer/en/doc/HarmonyOS-Guides/restricted-permissions/)
