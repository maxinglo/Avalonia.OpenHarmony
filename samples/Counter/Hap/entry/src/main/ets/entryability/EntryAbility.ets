import { Configuration, UIAbility } from '@kit.AbilityKit';
import { window } from '@kit.ArkUI';
import { hilog } from '@kit.PerformanceAnalysisKit';
import bridge from 'libavalonia_ohos_bridge.so';
import { registerSystemRequests } from '../services/SystemRequests';
import { StatusBar } from '../services/StatusBar';
import { reportStageDestroyed } from './ChildWindows';
import { HostLifecycle, PlatformWindow, updateTheme } from './PlatformWindow';

export default class EntryAbility extends UIAbility {
  private platformWindow: PlatformWindow | undefined = undefined;
  private stopSystemRequests: (() => void) | undefined = undefined;
  private foreground: boolean | undefined = undefined;
  private configured: boolean = false;

  onWindowStageCreate(windowStage: window.WindowStage): void {
    try {
      bridge.configureFiles(this.context.filesDir);
      const mainWindow = windowStage.getMainWindowSync();
      bridge.configureInputMethod(mainWindow.getWindowProperties().id);
      const statusBar = new StatusBar(this.context);
      const stopRequests = registerSystemRequests(this.context, mainWindow, windowStage, false, true, true,
        (operation: string, argument: string): Promise<string | null> => statusBar.execute(operation, argument));
      this.stopSystemRequests = (): void => {
        statusBar.stop();
        stopRequests();
      };
      bridge.reportTrayEvent(0n, '');
      updateTheme(this.context.config);
      this.configured = true;
      if (this.foreground !== undefined) {
        this.reportForeground(this.foreground);
      }
      this.platformWindow = new PlatformWindow(windowStage, mainWindow);
      this.platformWindow.start();
      windowStage.loadContent('pages/Index', (error) => {
        if (error.code) {
          hilog.error(0x0202, 'AvaloniaOHOS', 'loadContent: %{public}d', error.code);
        }
      });
    } catch (error) {
      hilog.error(0x0202, 'AvaloniaOHOS', '%{public}s', (error as Error).message);
    }
  }

  onWindowStageWillDestroy(_windowStage: window.WindowStage): void {
    this.platformWindow?.stop();
    this.platformWindow = undefined;
    this.configured = false;
    this.stopSystemRequests?.();
    this.stopSystemRequests = undefined;
  }

  onWindowStageDestroy(): void {
    reportStageDestroyed();
  }

  onForeground(): void {
    this.reportForeground(true);
  }

  onBackground(): void {
    this.reportForeground(false);
  }

  onConfigurationUpdate(config: Configuration): void {
    if (this.configured) {
      updateTheme(config);
    }
  }

  private reportForeground(foreground: boolean): void {
    this.foreground = foreground;
    if (this.configured) {
      bridge.setLifecycle(foreground ? HostLifecycle.ApplicationForeground : HostLifecycle.ApplicationBackground);
    }
  }
}
