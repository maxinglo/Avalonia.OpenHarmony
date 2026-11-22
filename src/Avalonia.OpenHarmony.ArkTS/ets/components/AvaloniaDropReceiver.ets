import { unifiedDataChannel, uniformDataStruct, uniformTypeDescriptor } from '@kit.ArkData';
import { fileIo } from '@kit.CoreFileKit';
import { hilog } from '@kit.PerformanceAnalysisKit';
import bridge from 'libavalonia_ohos_bridge.so';

const textType = uniformTypeDescriptor.UniformDataType.PLAIN_TEXT;
const fileType = uniformTypeDescriptor.UniformDataType.FILE;
const fileUriType = uniformTypeDescriptor.UniformDataType.FILE_URI;
export const avaloniaDropTypes: string[] = [textType, fileType, fileUriType];
const maxItems = 32;
const maxPayloadLength = 1_048_576;

interface DroppedFile {
  uri: string;
  descriptor: number;
  length: number;
}

interface DropPayload {
  texts: string[];
  files: DroppedFile[];
}

export class AvaloniaDropReceiver {
  dispatch(surfaceId: string, phase: number, x: number, y: number, event: DragEvent): void {
    const modifiers = (event.getModifierKeyState?.(['Alt']) ? 1 : 0)
      | (event.getModifierKeyState?.(['Ctrl']) ? 2 : 0)
      | (event.getModifierKeyState?.(['Shift']) ? 4 : 0);
    if (phase === 3) {
      this.drop(surfaceId, x, y, modifiers, event);
      return;
    }
    const summary = event.getSummary().summary;
    let formats = 0;
    if (summary[textType] !== undefined) {
      formats |= 1;
    }
    if (summary[fileType] !== undefined || summary[fileUriType] !== undefined) {
      formats |= 2;
    }
    const accepted = bridge.drag(surfaceId, phase, x, y, modifiers, formats, '');
    event.dragBehavior = DragBehavior.COPY;
    event.setResult(accepted ? DragResult.DROP_ENABLED : DragResult.DROP_DISABLED);
  }

  private drop(surfaceId: string, x: number, y: number, modifiers: number, event: DragEvent): void {
    const opened: fileIo.File[] = [];
    let accepted = false;
    try {
      const records = event.getData().getRecords();
      const payload: DropPayload = { texts: [], files: [] };
      let textLength = 0;
      if (records.length <= maxItems) {
        for (const record of records) {
          const types = record.getTypes();
          if (types.includes(textType)) {
            const text = this.readText(record);
            if (text !== undefined) {
              textLength += text.length;
              if (textLength > maxPayloadLength) {
                break;
              }
              payload.texts.push(text);
            }
          }
          const uri = this.readFileUri(record, types);
          if (uri !== undefined) {
            this.openFile(uri, opened, payload.files);
          }
        }
      }
      if (textLength <= maxPayloadLength && payload.texts.length + payload.files.length <= maxItems) {
        accepted = bridge.drag(surfaceId, 3, x, y, modifiers, 0, JSON.stringify(payload));
      }
    } catch (error) {
      hilog.warn(0x0202, 'AvaloniaOHOS', 'Could not read the system drop data');
    } finally {
      for (const file of opened) {
        try {
          fileIo.closeSync(file);
        } catch (error) {
          hilog.warn(0x0202, 'AvaloniaOHOS', 'Could not close a dropped file descriptor');
        }
      }
    }
    if (!accepted) {
      bridge.drag(surfaceId, 2, x, y, modifiers, 0, '');
    }
    event.dragBehavior = DragBehavior.COPY;
    event.setResult(accepted ? DragResult.DRAG_SUCCESSFUL : DragResult.DRAG_FAILED);
  }

  private readText(record: unifiedDataChannel.UnifiedRecord): string | undefined {
    if (record instanceof unifiedDataChannel.PlainText) {
      return record.textContent;
    }
    const entry = record.getEntry(textType);
    if (typeof entry === 'string') {
      return entry;
    }
    if (typeof entry === 'object' && entry !== null) {
      const value = (entry as uniformDataStruct.PlainText).textContent;
      return typeof value === 'string' ? value : undefined;
    }
    return undefined;
  }

  private readFileUri(record: unifiedDataChannel.UnifiedRecord, types: string[]): string | undefined {
    if (types.includes(fileUriType)) {
      const entry = record.getEntry(fileUriType);
      if (typeof entry === 'object' && entry !== null) {
        const file = entry as uniformDataStruct.FileUri;
        if (typeof file.oriUri === 'string' && file.fileType !== uniformTypeDescriptor.UniformDataType.FOLDER) {
          return file.oriUri;
        }
      }
    }
    if (types.includes(fileType) && record instanceof unifiedDataChannel.File) {
      return record.uri;
    }
    return undefined;
  }

  private openFile(uri: string, opened: fileIo.File[], files: DroppedFile[]): void {
    if (!uri.toLowerCase().startsWith('file://') || uri.length > 4096 || uri.includes('\u0000') ||
      uri.endsWith('/') || uri.includes('?') || uri.includes('#')) {
      return;
    }
    try {
      // The source may have granted only one-time read access. FileKit checks that grant here.
      const file = fileIo.openSync(uri, fileIo.OpenMode.READ_ONLY | fileIo.OpenMode.NONBLOCK);
      opened.push(file);
      const stat = fileIo.statSync(file.fd);
      if (stat.isFile()) {
        files.push({ uri: uri, descriptor: file.fd, length: stat.size });
      }
    } catch (error) {
      hilog.warn(0x0202, 'AvaloniaOHOS', 'A dropped file could not be opened for reading');
    }
  }
}
