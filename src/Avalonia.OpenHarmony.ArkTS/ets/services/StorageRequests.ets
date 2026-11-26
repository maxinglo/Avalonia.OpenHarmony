import { abilityAccessCtrl, bundleManager, common, Permissions } from '@kit.AbilityKit';
import { uniformTypeDescriptor } from '@kit.ArkData';
import { BusinessError, deviceInfo } from '@kit.BasicServicesKit';
import { Environment, fileIo, fileShare, fileUri, picker } from '@kit.CoreFileKit';

const folderSelectionCapability: string = 'SystemCapability.FileManagement.UserFileService.FolderSelection';
const folderAuthorizationCapability: string = 'SystemCapability.FileManagement.AppFileService.FolderAuthorization';
const folderObtainCapability: string = 'SystemCapability.FileManagement.File.Environment.FolderObtain';

export function supportsFolderPicker(): boolean {
  return canIUse(folderSelectionCapability);
}

export function supportsPersistentBookmarks(): boolean {
  return bookmarkSetupError() === 0;
}

function bookmarkSetupError(): number {
  if (!canIUse(folderAuthorizationCapability)) {
    return 801;
  }
  try {
    return hasPermission('ohos.permission.FILE_ACCESS_PERSIST') ? 0 : 201;
  } catch (error) {
    return (error as BusinessError).code || 201;
  }
}

function hasPermission(permission: Permissions): boolean {
  const token = bundleManager.getBundleInfoForSelfSync(bundleManager.BundleFlag.GET_BUNDLE_INFO_WITH_APPLICATION)
    .appInfo.accessTokenId;
  return abilityAccessCtrl.createAtManager().checkAccessTokenSync(token, permission) ===
    abilityAccessCtrl.GrantStatus.PERMISSION_GRANTED;
}

interface PickerRequest {
  startUri: string | null;
  multiple: boolean;
  filters: string[];
  fileName: string | null;
  requestBookmarks: boolean;
}

interface PickerEntry {
  uri: string;
  name: string;
  bookmarkable: boolean;
}

interface PickerReply {
  entries: PickerEntry[];
  selectedIndex: number;
}

interface StatReply {
  error: number;
  name?: string;
  directory?: boolean;
  size?: number;
  modifiedSeconds?: number;
  modifiedNanoseconds?: string;
}

interface BookmarkRequest {
  uri: string;
  access: number;
  directory: boolean;
}

interface BookmarkReply {
  error: number;
  policyError?: number;
}

type CompleteRequest = (value: string | null, fd?: number) => void;

function target(uri: string): string {
  // A plain file:/// URI represents a local app-sandbox path. Picker URIs retain their authorities and grants.
  return uri.startsWith('file:///') ? new fileUri.FileUri(uri).path : uri;
}

async function wellKnownFolder(name: string): Promise<PickerEntry | null> {
  if (!canIUse(folderObtainCapability)) {
    return null;
  }
  let permission: Permissions;
  let path: string;
  switch (name) {
    case 'desktop':
      permission = 'ohos.permission.READ_WRITE_DESKTOP_DIRECTORY';
      path = Environment.getUserDesktopDir();
      break;
    case 'documents':
      permission = 'ohos.permission.READ_WRITE_DOCUMENTS_DIRECTORY';
      path = Environment.getUserDocumentDir();
      break;
    case 'downloads':
      permission = 'ohos.permission.READ_WRITE_DOWNLOAD_DIRECTORY';
      path = Environment.getUserDownloadDir();
      break;
    default:
      return null;
  }

  if (!hasPermission(permission) || !path.startsWith('/')) {
    return null;
  }
  // The Environment API exposes the path before the user has authorized access to it.
  const authority = path.startsWith('/storage/Users/currentUser/') ? 'docs' : '';
  const encodedPath = path.replace(/\/+$/, '').split('/').map((segment: string): string =>
    encodeURIComponent(segment)).join('/');
  const uri = `file://${authority}${encodedPath}`;
  const directory = await fileIo.open(target(uri), fileIo.OpenMode.READ_ONLY | fileIo.OpenMode.DIR |
    fileIo.OpenMode.NOFOLLOW);
  await fileIo.close(directory);
  return { uri: uri, name: new fileUri.FileUri(uri).name, bookmarkable: false };
}

function resolveMimeFilters(groups: string[][]): string[][] {
  const supportsTypes = canIUse('SystemCapability.DistributedDataManager.UDMF.Core');
  return groups.map((group: string[]): string[] => {
    const suffixes = new Set<string>();
    for (const mimeType of group) {
      const mime = mimeType.trim().toLowerCase();
      if (mime === '*/*') {
        suffixes.add('.*');
      } else if (supportsTypes) {
        try {
          const ids = uniformTypeDescriptor.getUniformDataTypesByMIMEType(mime);
          for (const id of ids) {
            uniformTypeDescriptor.getTypeDescriptor(id).filenameExtensions.forEach((extension: string): void => {
              suffixes.add(extension);
            });
          }
        } catch (_error) {
          // Unregistered types have no suffixes the document picker can use.
        }
      }
    }
    return Array.from(suffixes);
  });
}

async function pick(context: common.UIAbilityContext, operation: string, request: PickerRequest): Promise<PickerReply> {
  const documentPicker = new picker.DocumentViewPicker(context);
  const folderSupport = canIUse(folderSelectionCapability);
  let uris: string[];
  let selectedIndex = -1;
  if (operation === 'storage.save') {
    const options = new picker.DocumentSaveOptions();
    if (request.startUri !== null) {
      options.defaultFilePathUri = request.startUri;
    }
    if (request.fileName !== null && request.fileName.length > 0) {
      options.newFileNames = [request.fileName];
    }
    if (request.filters.length > 0) {
      options.fileSuffixChoices = request.filters;
    }
    uris = await documentPicker.save(options);
    if (folderSupport && uris.length > 0 && request.filters.length > 0) {
      try {
        selectedIndex = documentPicker.getSelectedIndex();
      } catch (_error) {
        // The saved URI remains usable when the system provider has no selected type.
      }
    }
  } else {
    const selectingFolders = operation === 'storage.selectFolder';
    if (selectingFolders && !folderSupport) {
      return { entries: [], selectedIndex: -1 };
    }
    const options = new picker.DocumentSelectOptions();
    if (request.startUri !== null) {
      options.defaultFilePathUri = request.startUri;
    }
    const multiple = request.multiple && (!selectingFolders || deviceInfo.deviceType === '2in1');
    if (!multiple) {
      options.maxSelectNumber = 1;
    }
    if (selectingFolders) {
      options.selectMode = picker.DocumentSelectMode.FOLDER;
      if (multiple) {
        options.allowsMulFolderSelection = true;
      }
    } else if (request.filters.length > 0) {
      options.fileSuffixFilters = request.filters;
    }
    uris = await documentPicker.select(options);
  }
  const entries: PickerEntry[] = uris.map((uri: string): PickerEntry => {
    const selectedFile = new fileUri.FileUri(uri);
    return { uri: uri, name: selectedFile.name, bookmarkable: false };
  });
  if (request.requestBookmarks && supportsPersistentBookmarks()) {
    for (const entry of entries) {
      try {
        entry.bookmarkable = !new fileUri.FileUri(entry.uri).isRemoteUri();
      } catch (_error) {
        // The system may return a readable URI that cannot be persisted.
      }
    }
  }
  return { entries: entries, selectedIndex: selectedIndex };
}

function fileError(error: BusinessError): string {
  const result: StatReply = { error: error.code || 13900042 };
  return JSON.stringify(result);
}

async function openStream(uri: string, operation: string, complete: CompleteRequest): Promise<void> {
  let mode = operation === 'storage.openRead' || operation === 'storage.openMutationDirectory'
    ? fileIo.OpenMode.READ_ONLY
    : fileIo.OpenMode.WRITE_ONLY | fileIo.OpenMode.TRUNC;
  if (operation === 'storage.createFile') {
    mode |= fileIo.OpenMode.CREATE;
  }
  if (operation === 'storage.openMutationDirectory') {
    mode |= fileIo.OpenMode.DIR | fileIo.OpenMode.NOFOLLOW;
  }
  let file: fileIo.File;
  try {
    file = await fileIo.open(target(uri), mode);
  } catch (error) {
    complete(fileError(error as BusinessError));
    return;
  }
  try {
    // The bridge duplicates the descriptor synchronously. The ArkTS File always releases its own descriptor.
    complete('', file.fd);
  } finally {
    await fileIo.close(file);
  }
}

async function stat(uri: string): Promise<string> {
  try {
    const info = await fileIo.stat(target(uri));
    const result: StatReply = {
      error: 0,
      name: new fileUri.FileUri(uri).name,
      directory: info.isDirectory(),
      modifiedSeconds: info.mtime
    };
    if (info.isFile()) {
      result.size = info.size;
    }
    try {
      const nanoseconds = info.mtimeNs;
      if (nanoseconds !== undefined) {
        result.modifiedNanoseconds = nanoseconds.toString();
      }
    } catch (_error) {
      // Some file providers only return the seconds timestamp.
    }
    return JSON.stringify(result);
  } catch (error) {
    return fileError(error as BusinessError);
  }
}

async function bookmark(operation: string, request: BookmarkRequest): Promise<BookmarkReply> {
  const error = bookmarkSetupError();
  if (error !== 0) {
    return { error: error };
  }

  const read = fileShare.OperationMode.READ_MODE;
  const readWrite = read | fileShare.OperationMode.WRITE_MODE;
  if (!request.uri.startsWith('file://') || (request.access !== read && request.access !== readWrite)) {
    return { error: 401 };
  }
  try {
    if (new fileUri.FileUri(request.uri).isRemoteUri()) {
      return { error: 13900001, policyError: fileShare.PolicyErrorCode.PERSISTENCE_FORBIDDEN };
    }
    const policy: fileShare.PolicyInfo = { uri: request.uri, operationMode: request.access };
    const policies: fileShare.PolicyInfo[] = [policy];
    const persisted = (await fileShare.checkPersistentPermission(policies))[0] === true;
    if (operation === 'storage.bookmark.persist' && !persisted) {
      await fileShare.persistPermission(policies);
    } else if (operation === 'storage.bookmark.activate') {
      if (!persisted) {
        return { error: 13900001, policyError: fileShare.PolicyErrorCode.PERMISSION_NOT_PERSISTED };
      }
      await fileShare.activatePermission(policies);
    } else if (operation === 'storage.bookmark.revoke' && persisted) {
      await fileShare.revokePermission(policies);
    }
    return { error: 0 };
  } catch (failure) {
    const systemError = failure as BusinessError<fileShare.PolicyErrorResult[]>;
    const result: BookmarkReply = { error: systemError.code || 13900042 };
    if (systemError.data && systemError.data.length > 0) {
      result.policyError = systemError.data[0].code;
      if (operation === 'storage.bookmark.revoke' &&
        result.policyError === fileShare.PolicyErrorCode.PERMISSION_NOT_PERSISTED) {
        return { error: 0 };
      }
    }
    return result;
  }
}

export async function executeStorageRequest(context: common.UIAbilityContext, operation: string, argument: string,
  complete: CompleteRequest): Promise<void> {
  switch (operation) {
    case 'storage.wellKnownFolder': {
      try {
        const folder = await wellKnownFolder(argument);
        complete(folder === null ? null : JSON.stringify(folder));
      } catch (_error) {
        complete(null);
      }
      return;
    }
    case 'storage.resolveMimeFilters':
      complete(JSON.stringify(resolveMimeFilters(JSON.parse(argument) as string[][])));
      return;
    case 'storage.select':
    case 'storage.selectFolder':
    case 'storage.save': {
      const request = JSON.parse(argument) as PickerRequest;
      complete(JSON.stringify(await pick(context, operation, request)));
      return;
    }
    case 'storage.openRead':
    case 'storage.openWrite':
    case 'storage.openMutationDirectory':
    case 'storage.createFile':
      await openStream(argument, operation, complete);
      return;
    case 'storage.stat':
      complete(await stat(argument));
      return;
    case 'storage.bookmark.persist':
    case 'storage.bookmark.activate':
    case 'storage.bookmark.revoke': {
      const request = JSON.parse(argument) as BookmarkRequest;
      complete(JSON.stringify(await bookmark(operation, request)));
      return;
    }
    default:
      complete(null);
  }
}
