import { emitter } from '@kit.BasicServicesKit';
import { statusBarManager } from '@kit.DeskTopExtensionKit';
import { image } from '@kit.ImageKit';

interface TrayMenuEntry {
  title: string;
  code?: string;
  selected: boolean;
  icon?: string;
  children?: TrayMenuEntry[];
}

export async function getTrayMenuGroups(json: string, abilityName: string, moduleName: string,
  createIcon: (png: string) => Promise<image.PixelMap>, icons: image.PixelMap[]): Promise<statusBarManager.StatusBarGroupMenu[]> {
  const entries = JSON.parse(json) as TrayMenuEntry[][];
  const groups: statusBarManager.StatusBarGroupMenu[] = [];
  const action: statusBarManager.StatusBarMenuAction = { abilityName, moduleName, notifyOnly: true };
  for (const entriesInGroup of entries) {
    const group: statusBarManager.StatusBarGroupMenu = [];
    for (const entry of entriesInGroup) {
      const options = await getOptions(entry, createIcon, icons);
      if (entry.children !== undefined) {
        const subMenu: statusBarManager.StatusBarSubMenuItem[] = [];
        for (const child of entry.children) {
          if (child.code !== undefined) {
            subMenu.push({
              subTitle: child.title,
              menuCode: child.code,
              menuAction: action,
              options: await getOptions(child, createIcon, icons)
            });
          }
        }
        group.push({ title: entry.title, subMenu, options });
      } else if (entry.code !== undefined) {
        group.push({
          title: entry.title,
          menuCode: entry.code,
          menuAction: action,
          options
        });
      }
    }
    groups.push(group);
  }
  return groups;
}

async function getOptions(entry: TrayMenuEntry, createIcon: (png: string) => Promise<image.PixelMap>,
  icons: image.PixelMap[]): Promise<statusBarManager.StatusBarMenuItemOptions> {
  const options: statusBarManager.StatusBarMenuItemOptions = { selected: entry.selected };
  if (entry.icon !== undefined) {
    const icon = await createIcon(entry.icon);
    icons.push(icon);
    options.icon = { white: icon, black: icon };
  }
  return options;
}

export function getTrayMenuCode(event: emitter.EventData): string | undefined {
  const code = event.data?.['menuCode'] as string | undefined;
  return typeof code === 'string' && code.length > 0 ? code : undefined;
}
