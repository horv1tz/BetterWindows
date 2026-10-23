import { appById, appByName } from '../fixtures';
import type { MockHost } from '../host';

/**
 * FROZEN (scaffold): the foreground app. Other mock domains change it with  (host as any).setActiveApp(appId | null)
 * (e.g. the dock mock when an app is activated). Pushed to menubar/overlay/desktop surfaces on ready.
 */
export default function register(host: MockHost) {
  let current: string | null = appByName('File Explorer').id;
  const state = () => {
    const a = current ? appById(current) : undefined;
    return { app: a ? { appId: a.id, name: a.name, iconUrl: a.iconUrl } : undefined };
  };
  (host as any).setActiveApp = (appId: string | null) => { current = appId; host.emit('activeApp.changed', state()); };
  (host as any).getActiveApp = () => current;
  host.provideState('activeApp.changed', () => state());
}
