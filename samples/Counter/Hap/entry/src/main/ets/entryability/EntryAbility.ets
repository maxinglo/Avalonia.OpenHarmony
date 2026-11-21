import { UIAbility } from '@kit.AbilityKit';
import { window } from '@kit.ArkUI';
import { hilog } from '@kit.PerformanceAnalysisKit';
import bridge from 'libavalonia_ohos_bridge.so';

export default class EntryAbility extends UIAbility {
  onWindowStageCreate(windowStage: window.WindowStage): void {
    try {
      bridge.configureFiles(this.context.filesDir);
      bridge.configureInputMethod(windowStage.getMainWindowSync().getWindowProperties().id);
      windowStage.loadContent('pages/Index', (error) => {
        if (error.code) {
          hilog.error(0x0202, 'AvaloniaOHOS', 'loadContent: %{public}d', error.code);
        }
      });
    } catch (error) {
      hilog.error(0x0202, 'AvaloniaOHOS', '%{public}s', (error as Error).message);
    }
  }
}
