import { APPS, appById } from '../fixtures';
import type { MockHost } from '../host';

/** FROZEN (scaffold): the installed-app catalog (apps.list / apps.launch / apps.reveal / apps.changed). */
export default function register(host: MockHost) {
  host.handle('apps.list', () => ({ apps: APPS }));
  host.handle('apps.launch', (p) => { console.info('[mock] launch', appById(p.appId)?.name ?? p.appId); });
  host.handle('apps.reveal', (p) => { console.info('[mock] reveal in explorer', p.appId); });
  host.provideState('apps.changed', () => ({ apps: APPS }));
}
