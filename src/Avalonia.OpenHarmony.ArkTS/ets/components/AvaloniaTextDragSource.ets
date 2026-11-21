interface DragRequest {
  gesture: number;
  text: string;
}

interface DragGesture {
  pointerId: number;
  source: AvaloniaTextDragSource;
}

interface DragSession {
  source: AvaloniaTextDragSource;
  finish: (copy: boolean) => void;
}

// Negative, unique contact IDs are internal to the Avalonia bridge. ArkUI receives the original touch ID.
let nextContact = -1;
const eligibleGestures: Map<number, DragGesture> = new Map();
let activeDrag: DragSession | undefined;

export class AvaloniaTextDragSource {
  private contacts: Map<number, number> = new Map();
  private startDrag: (pointerId: number, text: string) => Promise<boolean>;

  constructor(startDrag: (pointerId: number, text: string) => Promise<boolean>) {
    this.startDrag = startDrag;
  }

  contact(pointerId: number, phase: number): number {
    const existing = this.contacts.get(pointerId);
    if (phase === 0 && Number.isInteger(pointerId) && pointerId >= 0 && pointerId <= 9) {
      if (existing !== undefined) {
        eligibleGestures.delete(existing);
      }
      const token = nextContact--;
      this.contacts.set(pointerId, token);
      eligibleGestures.set(token, { pointerId, source: this });
      return token;
    }
    if (existing !== undefined && (phase === 2 || phase === 3)) {
      this.contacts.delete(pointerId);
      eligibleGestures.delete(existing);
    }
    return existing ?? pointerId;
  }

  revokeGestures(): void {
    for (const token of this.contacts.values()) {
      eligibleGestures.delete(token);
    }
  }

  surfaceDestroyed(): void {
    this.revokeGestures();
    this.contacts.clear();
    if (activeDrag?.source === this) {
      // Detach the managed waiter. The system will end its own drag and may still call its Promise.
      activeDrag.finish(false);
    }
  }

  cancelContacts(): number[] {
    const canceled: number[] = [];
    for (const token of this.contacts.values()) {
      eligibleGestures.delete(token);
      canceled.push(token);
    }
    this.contacts.clear();
    return canceled;
  }

  static async execute(argument: string): Promise<string | null> {
    const request = JSON.parse(argument) as DragRequest;
    if (!Number.isSafeInteger(request.gesture) || typeof request.text !== 'string' ||
      request.text.length > 64 * 1024) {
      return null;
    }
    const gesture = eligibleGestures.get(request.gesture);
    if (gesture === undefined) {
      return null;
    }
    eligibleGestures.delete(request.gesture);
    if (activeDrag !== undefined) {
      return null;
    }
    return new Promise<string | null>((resolve: (value: string | null) => void): void => {
      const session: DragSession = {
        source: gesture.source,
        finish: (copy: boolean): void => resolve(copy ? 'copy' : null)
      };
      activeDrag = session;
      const settle = (copy: boolean): void => {
        activeDrag = undefined;
        session.finish(copy);
      };
      try {
        gesture.source.startDrag(gesture.pointerId, request.text).then(settle, (): void => settle(false));
      } catch (_error) {
        settle(false);
      }
    });
  }
}
