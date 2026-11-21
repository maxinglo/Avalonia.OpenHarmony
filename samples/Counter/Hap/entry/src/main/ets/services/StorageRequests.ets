import { common } from '@kit.AbilityKit';
import { BusinessError, deviceInfo } from '@kit.BasicServicesKit';
import { fileIo, fileUri, picker } from '@kit.CoreFileKit';

const folderSelectionCapability: string = 'SystemCapability.FileManagement.UserFileService.FolderSelection';

interface PickerRequest {
  startUri: string | null;
  multiple: boolean;
  filters: string[];
  fileName: string | null;
}

interface PickerEntry {
  uri: string;
  name: string;
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
}

type CompleteRequest = (value: string | null, fd?: number) => void;

function target(uri: string): string {
  // A plain file:/// URI represents a local app-sandbox path. Picker URIs retain their authorities and grants.
  return uri.startsWith('file:///') ? new fileUri.FileUri(uri).path : uri;
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
    return { uri: uri, name: new fileUri.FileUri(uri).name };
  });
  return { entries: entries, selectedIndex: selectedIndex };
}

function fileError(error: BusinessError): string {
  const result: StatReply = { error: error.code || 13900042 };
  return JSON.stringify(result);
}

async function openStream(uri: string, operation: string, complete: CompleteRequest): Promise<void> {
  let mode = operation === 'storage.openRead'
    ? fileIo.OpenMode.READ_ONLY
    : fileIo.OpenMode.WRITE_ONLY | fileIo.OpenMode.TRUNC;
  if (operation === 'storage.createFile') {
    mode |= fileIo.OpenMode.CREATE;
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
    return JSON.stringify(result);
  } catch (error) {
    return fileError(error as BusinessError);
  }
}

export async function executeStorageRequest(context: common.UIAbilityContext, operation: string, argument: string,
  complete: CompleteRequest): Promise<void> {
  switch (operation) {
    case 'storage.select':
    case 'storage.selectFolder':
    case 'storage.save': {
      const request = JSON.parse(argument) as PickerRequest;
      complete(JSON.stringify(await pick(context, operation, request)));
      return;
    }
    case 'storage.openRead':
    case 'storage.openWrite':
    case 'storage.createFile':
      await openStream(argument, operation, complete);
      return;
    case 'storage.stat':
      complete(await stat(argument));
      return;
    default:
      complete(null);
  }
}
