import { common, Want, wantConstant } from '@kit.AbilityKit';
import { uniformTypeDescriptor } from '@kit.ArkData';
import { fileUri } from '@kit.CoreFileKit';
import { hilog } from '@kit.PerformanceAnalysisKit';
import bridge from 'libavalonia_ohos_bridge.so';

export function registerSystemRequests(context: common.UIAbilityContext): void {
  bridge.registerSystemRequests((id: bigint, operation: string, argument: string): void => {
    executeLauncherRequest(context, operation, argument)
      .then((value: string | null): void => bridge.completeSystemRequest(id, value))
      .catch((error: Error): void => {
        hilog.warn(0x0202, 'AvaloniaOHOS', 'System request %{public}s failed', operation);
        bridge.completeSystemRequest(id, null);
      });
  });
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
