import { common, Want, wantConstant } from '@kit.AbilityKit';
import { uniformTypeDescriptor } from '@kit.ArkData';
import { fileUri } from '@kit.CoreFileKit';
import { hilog } from '@kit.PerformanceAnalysisKit';
import { window } from '@kit.ArkUI';
import bridge from 'libavalonia_ohos_bridge.so';
import { executeStorageRequest, supportsFolderPicker } from './StorageRequests';
import { WindowInsetsRequests } from '../entryability/WindowInsets';
import { ChildWindows } from '../entryability/ChildWindows';
import { AvaloniaTextDragSource } from '../components/AvaloniaTextDragSource';

export function registerSystemRequests(context: common.UIAbilityContext, mainWindow?: window.Window,
  stage?: window.WindowStage, enableChildAccessibility: boolean = false,
  enableChildInboundDrop: boolean = false, enableChildTextDragSource: boolean = false,
  trayRequests?: (operation: string, argument: string) => Promise<string | null>): () => void {
  const windowInsets = mainWindow === undefined ? undefined : new WindowInsetsRequests(mainWindow);
  const children = mainWindow === undefined || stage === undefined ? undefined
    : new ChildWindows(stage, mainWindow, enableChildAccessibility, enableChildInboundDrop, enableChildTextDragSource);
  bridge.registerSystemRequests((id: bigint, operation: string, argument: string): void => {
    const finish = (value: string | null, fd?: number): void => bridge.completeSystemRequest(id, value, fd);
    let request: Promise<void>;
    if (operation.startsWith('storage.')) {
      request = executeStorageRequest(context, operation, argument, finish);
    } else if (operation === 'drag.text' || operation === 'drag.texts' || operation === 'drag.data') {
      request = AvaloniaTextDragSource.execute(operation, argument)
        .then((value: string | null): void => finish(value));
    } else if (operation === 'window.child' && children !== undefined) {
      request = children.execute(argument).then((value: string | null): void => finish(value));
    } else if (operation.startsWith('window.') && windowInsets !== undefined) {
      request = windowInsets.execute(operation, argument).then((value: string | null): void => finish(value));
    } else if (operation.startsWith('tray.') && trayRequests !== undefined) {
      request = trayRequests(operation, argument).then((value: string | null): void => finish(value));
    } else {
      request = executeLauncherRequest(context, operation, argument).then((value: string | null): void => finish(value));
    }
    request.catch((_error: Error): void => {
      hilog.warn(0x0202, 'AvaloniaOHOS', 'System request %{public}s failed', operation);
      finish(null);
    });
  }, supportsFolderPicker());
  return (): void => {
    children?.stop();
    windowInsets?.stop();
    bridge.unregisterSystemRequests();
  };
}

async function executeLauncherRequest(context: common.UIAbilityContext, operation: string,
  argument: string): Promise<string | null> {
  if (operation === 'launcher.uri') {
    await context.openLink(argument, { appLinkingOnly: false });
    return '';
  }
  if (operation === 'launcher.mailto') {
    await context.startAbility({ action: wantConstant.Action.ACTION_SEND_TO_DATA, uri: argument });
    return '';
  }

  const folder = operation === 'launcher.folder';
  if (!folder && operation !== 'launcher.file' && operation !== 'launcher.path') {
    return null;
  }

  let uri = argument;
  if (operation === 'launcher.path') {
    uri = fileUri.getUriFromPath(argument);
  } else if (folder && argument.startsWith('file:///')) {
    uri = fileUri.getUriFromPath(new fileUri.FileUri(argument).path);
  }
  const want: Want = {
    action: 'ohos.want.action.viewData',
    uri: uri,
    flags: wantConstant.Flags.FLAG_AUTH_READ_URI_PERMISSION
  };
  if (folder) {
    want.type = uniformTypeDescriptor.UniformDataType.FOLDER;
  } else {
    const name = new fileUri.FileUri(uri).name;
    const extension = name.lastIndexOf('.');
    if (extension > 0) {
      try {
        want.type = uniformTypeDescriptor.getUniformDataTypeByFilenameExtension(name.substring(extension));
      } catch (_error) {
        // The system can infer the type from the URI suffix.
      }
    }
  }
  await context.startAbility(want);
  return '';
}
