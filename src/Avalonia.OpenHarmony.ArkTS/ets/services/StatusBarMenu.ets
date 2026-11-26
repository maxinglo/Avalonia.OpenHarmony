import { emitter } from '@kit.BasicServicesKit';
import { statusBarManager } from '@kit.DeskTopExtensionKit';

interface TrayMenuEntry {
  title: string;
  code?: string;
  selected: boolean;
  children?: TrayMenuEntry[];
}

export function getTrayMenuGroups(json: string, abilityName: string,
  moduleName: string): statusBarManager.StatusBarGroupMenu[] {
  const entries = JSON.parse(json) as TrayMenuEntry[][];
  const groups: statusBarManager.StatusBarGroupMenu[] = [];
  const action: statusBarManager.StatusBarMenuAction = { abilityName, moduleName, notifyOnly: true };
  for (const entriesInGroup of entries) {
    const group: statusBarManager.StatusBarGroupMenu = [];
    for (const entry of entriesInGroup) {
      if (entry.children !== undefined) {
        const subMenu: statusBarManager.StatusBarSubMenuItem[] = [];
        for (const child of entry.children) {
          if (child.code !== undefined) {
            subMenu.push({
              subTitle: child.title,
              menuCode: child.code,
              menuAction: action,
              options: { selected: child.selected }
            });
          }
        }
        group.push({ title: entry.title, subMenu });
      } else if (entry.code !== undefined) {
        group.push({
          title: entry.title,
          menuCode: entry.code,
          menuAction: action,
          options: { selected: entry.selected }
        });
      }
    }
    groups.push(group);
  }
  return groups;
}

export function getTrayMenuCode(event: emitter.EventData): string | undefined {
  const code = event.data?.['menuCode'] as string | undefined;
  return typeof code === 'string' && code.length > 0 ? code : undefined;
}
