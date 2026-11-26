import { common } from '@kit.AbilityKit';
import { BusinessError, emitter } from '@kit.BasicServicesKit';
import { statusBarManager } from '@kit.DeskTopExtensionKit';
import { image } from '@kit.ImageKit';
import { hilog } from '@kit.PerformanceAnalysisKit';
import { util } from '@kit.ArkTS';
import bridge from 'libavalonia_ohos_bridge.so';
import { getTrayMenuCode, getTrayMenuGroups } from './StatusBarMenu';

interface TrayState {
  id: string;
  visible: boolean;
  icon: string | null;
  toolTip: string | null;
  menu: string;
}

const iconSize: number = 24;

// HarmonyOS allows one status bar item per app, including while the UIAbility stage is being replaced.
let statusBarWork: Promise<void> = Promise.resolve();

export class StatusBar {
  private readonly context: common.UIAbilityContext;
  private supported: Promise<boolean> | undefined = undefined;
  private stopped: boolean = false;
  private shownId: string | undefined = undefined;
  private currentIcon: string | null = null;
  private currentTip: string = '';
  private currentMenu: string = '[]';
  private iconListener: boolean = false;
  private menuListener: boolean = false;
  private readonly click = (_event: emitter.EventData): void => this.report('');
  private readonly menuClick = (event: emitter.EventData): void => {
    const code = getTrayMenuCode(event);
    if (code !== undefined) {
      this.report(code);
    }
  };

  constructor(context: common.UIAbilityContext) {
    this.context = context;
  }

  async execute(operation: string, argument: string): Promise<string | null> {
    if (operation === 'tray.supported') {
      return !this.stopped && await this.isSupported() ? 'true' : 'false';
    }
    if (operation !== 'tray.apply') {
      return null;
    }
    const apply = statusBarWork.then(async (): Promise<string | null> => {
      if (this.stopped) {
        return null;
      }
      if (!await this.isSupported()) {
        return 'unsupported';
      }
      await this.apply(JSON.parse(argument) as TrayState);
      return '';
    }).catch((error: BusinessError): null => {
      this.logFailure(error);
      return null;
    });
    statusBarWork = apply.then((): void => {});
    return apply;
  }

  stop(): void {
    this.stopped = true;
    statusBarWork = statusBarWork.then(async (): Promise<void> => {
      try {
        await this.remove();
      } catch (error) {
        this.logFailure(error as BusinessError);
      } finally {
        this.unsubscribe();
      }
    });
  }

  private isSupported(): Promise<boolean> {
    if (this.supported === undefined) {
      this.supported = this.checkSupport();
    }
    return this.supported;
  }

  private async checkSupport(): Promise<boolean> {
    try {
      if (canIUse('SystemCapability.PCService.StatusBarManager')) {
        return await statusBarManager.isStatusBarCapabilitySupported(this.context);
      }
    } catch (error) {
      this.logFailure(error as BusinessError);
    }
    return false;
  }

  private async apply(state: TrayState): Promise<void> {
    if (!state.visible) {
      if (this.shownId === state.id) {
        await this.remove();
      }
      return;
    }
    const tip = state.toolTip ? state.toolTip : await this.defaultToolTip();
    if (this.shownId !== state.id) {
      await this.remove();
      await this.add(state, tip);
      return;
    }
    if (this.currentIcon !== state.icon) {
      const icon = await this.createIcon(state.icon);
      try {
        await new Promise<void>((resolve, reject) => {
          statusBarManager.updateStatusBarIcon(this.context, { white: icon, black: icon },
            (error: BusinessError): void => error ? reject(error) : resolve());
        });
        this.currentIcon = state.icon;
        hilog.info(0x0202, 'AvaloniaOHOS', 'OpenHarmony status bar icon updated');
      } finally {
        await icon.release();
      }
    }
    if (this.currentTip !== tip) {
      await statusBarManager.updateStatusBarHoverTips(this.context, tip);
      this.currentTip = tip;
      hilog.info(0x0202, 'AvaloniaOHOS', 'OpenHarmony status bar hover tip updated');
    }
    if (this.currentMenu !== state.menu) {
      const menuIcons: image.PixelMap[] = [];
      try {
        const groups = await this.getMenu(state.menu, menuIcons);
        await new Promise<void>((resolve, reject) => {
          statusBarManager.updateStatusBarMenu(this.context, groups,
            (error: BusinessError): void => error ? reject(error) : resolve());
        });
      } finally {
        await this.releaseIcons(menuIcons);
      }
      this.currentMenu = state.menu;
      hilog.info(0x0202, 'AvaloniaOHOS', 'OpenHarmony status bar menu updated');
    }
  }

  private async add(state: TrayState, tip: string): Promise<void> {
    const icon = await this.createIcon(state.icon);
    const menuIcons: image.PixelMap[] = [];
    try {
      const item: statusBarManager.StatusBarItem = {
        icons: { white: icon, black: icon },
        quickOperation: { title: tip, height: iconSize, abilityName: '' },
        statusBarGroupMenu: await this.getMenu(state.menu, menuIcons),
        hoverTips: tip
      };
      this.subscribe();
      await new Promise<void>((resolve, reject) => {
        statusBarManager.addToStatusBar(this.context, item,
          (error: BusinessError): void => error ? reject(error) : resolve());
      });
      this.shownId = state.id;
      this.currentIcon = state.icon;
      this.currentTip = tip;
      this.currentMenu = state.menu;
      hilog.info(0x0202, 'AvaloniaOHOS', 'OpenHarmony status bar item added');
    } finally {
      if (this.shownId === undefined) {
        this.unsubscribe();
      }
      await icon.release();
      await this.releaseIcons(menuIcons);
    }
  }

  private async remove(): Promise<void> {
    if (this.shownId === undefined) {
      return;
    }
    await new Promise<void>((resolve, reject) => {
      statusBarManager.removeFromStatusBar(this.context,
        (error: BusinessError): void => error ? reject(error) : resolve());
    });
    this.shownId = undefined;
    this.unsubscribe();
    hilog.info(0x0202, 'AvaloniaOHOS', 'OpenHarmony status bar item removed');
  }

  private getMenu(json: string, icons: image.PixelMap[]): Promise<statusBarManager.StatusBarGroupMenu[]> {
    return getTrayMenuGroups(json, this.context.abilityInfo.name, this.context.abilityInfo.moduleName,
      (png: string): Promise<image.PixelMap> => this.createIcon(png), icons);
  }

  private async releaseIcons(icons: image.PixelMap[]): Promise<void> {
    for (const icon of icons) {
      await icon.release();
    }
  }

  private async defaultToolTip(): Promise<string> {
    const id = this.context.abilityInfo.labelId || this.context.applicationInfo.labelId;
    try {
      if (id !== 0) {
        const label = await this.context.resourceManager.getStringValue(id);
        if (label.length > 0) {
          return label.substring(0, 128);
        }
      }
    } catch (_error) {
      // The UIAbility name remains available when the label resource cannot be resolved.
    }
    return this.context.abilityInfo.name.substring(0, 128);
  }

  private async createIcon(png: string | null): Promise<image.PixelMap> {
    if (png !== null) {
      const bytes = new util.Base64Helper().decodeSync(png);
      const source = image.createImageSource(bytes.buffer as ArrayBuffer);
      try {
        const size = (await source.getImageInfo()).size;
        const scale = iconSize / Math.max(size.width, size.height);
        const desiredSize: image.Size = {
          width: Math.max(1, Math.round(size.width * scale)),
          height: Math.max(1, Math.round(size.height * scale))
        };
        return await source.createPixelMap({ desiredSize });
      } finally {
        await source.release();
      }
    }
    const id = this.context.abilityInfo.iconId || this.context.applicationInfo.iconId;
    const drawable = this.context.resourceManager.getDrawableDescriptor(id);
    const map = drawable.getPixelMap();
    try {
      const size = (await map.getImageInfo()).size;
      const scale = iconSize / Math.max(size.width, size.height);
      return await map.createScaledPixelMap(scale, scale);
    } finally {
      await map.release();
    }
  }

  private subscribe(): void {
    statusBarManager.on('statusBarIconClick', this.click);
    this.iconListener = true;
    statusBarManager.on('rightMenuClick', this.menuClick);
    this.menuListener = true;
  }

  private unsubscribe(): void {
    if (this.iconListener) {
      this.iconListener = false;
      try {
        statusBarManager.off('statusBarIconClick', this.click);
      } catch (error) {
        this.logFailure(error as BusinessError);
      }
    }
    if (this.menuListener) {
      this.menuListener = false;
      try {
        statusBarManager.off('rightMenuClick', this.menuClick);
      } catch (error) {
        this.logFailure(error as BusinessError);
      }
    }
  }

  private report(menuCode: string): void {
    if (!this.stopped && this.shownId !== undefined) {
      bridge.reportTrayEvent(BigInt(this.shownId), menuCode);
    }
  }

  private logFailure(error: BusinessError): void {
    hilog.warn(0x0202, 'AvaloniaOHOS', 'OpenHarmony status bar request failed: %{public}d %{public}s',
      error.code || 0, error.message);
  }
}
