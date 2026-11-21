import { common, Want, wantConstant } from '@kit.AbilityKit';
import { uniformTypeDescriptor } from '@kit.ArkData';
import { fileUri } from '@kit.CoreFileKit';
import { hilog } from '@kit.PerformanceAnalysisKit';
import { window } from '@kit.ArkUI';
import bridge from 'libavalonia_ohos_bridge.so';
import { executeStorageRequest, supportsFolderPicker } from './StorageRequests';
import { WindowInsetsRequests } from '../entryability/WindowInsets';

export function registerSystemRequests(context: common.UIAbilityContext, mainWindow?: window.Window): () => void {
  const windowInsets = mainWindow === undefined ? undefined : new WindowInsetsRequests(mainWindow);
  bridge.registerSystemRequests((id: bigint, operation: string, argument: string): void => {
    const finish = (value: string | null, fd?: number): void => bridge.completeSystemRequest(id, value, fd);
    let request: Promise<void>;
    if (operation.startsWith('storage.')) {
      request = executeStorageRequest(context, operation, argument, finish);
    } else if (operation.startsWith('window.') && windowInsets !== undefined) {
      request = windowInsets.execute(operation, argument).then((value: string | null): void => finish(value));
    } else {
      request = executeLauncherRequest(context, operation, argument).then((value: string | null): void => finish(value));
    }
    request.catch((_error: Error): void => {
      hilog.warn(0x0202, 'AvaloniaOHOS', 'System request %{public}s failed', operation);
      finish(null);
    });
  }, supportsFolderPicker());
  return (): void => {
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

  if (operation !== 'launcher.file' && operation !== 'launcher.path') {
    return null;
  }

  const uri = operation === 'launcher.path' ? fileUri.getUriFromPath(argument) : argument;
  const name = uri.substring(uri.lastIndexOf('/') + 1);
  const extension = name.lastIndexOf('.');
  const want: Want = {
    action: 'ohos.want.action.viewData',
    uri: uri,
    flags: wantConstant.Flags.FLAG_AUTH_READ_URI_PERMISSION
  };
  if (extension > 0) {
    want.type = uniformTypeDescriptor.getUniformDataTypeByFilenameExtension(name.substring(extension));
  }
  await context.startAbility(want);
  return '';
}
