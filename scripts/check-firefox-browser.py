"""Real Firefox extension/native-host checks in a disposable Marionette profile.
Usage: py scripts/check-firefox-browser.py PATH_TO_FIREFOX [--persistent-addon]
The second option requires a development/test Firefox that accepts unsigned extensions.
"""
import gc, json, pathlib, socket, sys, time, urllib.request, uuid, zipfile
from marionette_driver.marionette import Marionette
from marionette_driver.addons import Addons

root = pathlib.Path(__file__).resolve().parents[1]
output = root / 'output' / 'playwright'
meta = json.loads((output / 'broker.json').read_text(encoding='utf-8-sig'))
persistent = '--persistent-addon' in sys.argv
profile = output / ('firefox-persistent-check' if persistent else 'firefox-stable-check')
profile.mkdir(exist_ok=True)
addon = output / 'firefox-extension'
package = output / 'firefox-check.xpi'
with zipfile.ZipFile(package, 'w') as archive:
    for file in addon.iterdir():
        if file.is_file(): archive.write(file, file.name)

def control(path, data=None):
    request = urllib.request.Request(meta['url'] + path, json.dumps(data or {}).encode(),
        {'Authorization': meta['token'], 'Content-Type': 'application/json'})
    with urllib.request.urlopen(request, timeout=30) as response: return json.load(response)

def connected():
    for _ in range(40):
        clients = [c for c in control('/status') if c['browser'] == 'Firefox']
        if clients: return clients[0]
        time.sleep(.5)
    raise AssertionError('Firefox native host did not connect')

with socket.socket() as port_probe:
    port_probe.bind(('127.0.0.1', 0))
    port = port_probe.getsockname()[1]
m = Marionette(bin=sys.argv[1], port=port, profile=str(profile), startup_timeout=30,
    app_args=['--remote-allow-system-access'], gecko_log=str(output/'firefox-marionette.log'),
    prefs={'browser.startup.page':3, 'browser.sessionstore.resume_from_crash':True,
        **({'xpinstall.signatures.required':False} if persistent else {})})
m.start_session()
try:
    version = m.session_capabilities.get('browserVersion')
    print('Connected to Firefox '+version, flush=True)
    Addons(m).install(str(package), temp=not persistent)
    identity = connected()
    def extension_page():
        m.set_context('chrome')
        url = m.execute_script('return WebExtensionPolicy.getByID("browser@appswitcher.local").getURL("profile.html");')
        m.set_context('content')
        tab = m.open(type='tab', focus=True)
        m.switch_to_window(tab['handle'])
        m.navigate(url)
    extension_page()
    def api(expression):
        result = m.execute_async_script('const done=arguments[arguments.length-1]; const api=window.wrappedJSObject.browser; '
            '(async()=>{'+expression+'})().then(value=>done(JSON.stringify({value})),error=>done(JSON.stringify({error:String(error)})));')
        result = json.loads(result)
        assert 'error' not in result, result
        return result.get('value')
    app_id = str(uuid.uuid4())
    request = dict(action='ensure', appId=app_id, generation=str(uuid.uuid4()),
        url=meta['url']+'/fixture?run='+app_id, name='Firefox integration check', allowCreate=True, focus=True)
    def command(**changes): return control('/command', {'browser':'Firefox','request':{**request,**changes}})
    first = command()
    assert first['code'] == 'CreatedNew', (first, api('return {storage:await api.storage.local.get(null),tabs:await api.tabs.query({}),group:typeof api.tabs.group};'))
    api('return await api.tabs.create({url:'+json.dumps(request['url'])+'});')
    for i in range(20):
        api('return await api.windows.update('+str(first['windowId'])+',{state:"minimized"});')
        reply = command(allowCreate=False)
        assert reply['code'] == 'ActivatedExisting' and reply['tabId'] == first['tabId'], (i, reply)
    tabs = api('return await api.tabs.query({});')
    assert len([t for t in tabs if t.get('url') == request['url']]) == 2, tabs
    tag = api('return await api.sessions.getTabValue('+str(first['tabId'])+',"appswitcher.identity");')
    assert tag['appId'] == app_id, tag
    if persistent:
        m.restart(in_app=True)
        restored_identity = connected()
        assert restored_identity['profileId'] == identity['profileId'], 'Profile identity changed'
        extension_page()
        before = api('return await api.tabs.query({});')
        reply = command(allowCreate=False)
        assert reply['code'] == 'ActivatedExisting', reply
        after = api('return await api.tabs.query({});')
        assert len(before) == len(after), 'Restart created a tab'
        restored_tag = api('return await api.sessions.getTabValue('+str(reply['tabId'])+',"appswitcher.identity");')
        assert restored_tag == tag, restored_tag
    # Exercise Firefox's persistent tab-value identity independently of the visible marker.
    tabs = api('return await api.tabs.query({});')
    anchor = next(t for t in tabs if 'app='+app_id in t.get('url',''))
    api('await api.tabs.remove('+str(anchor['id'])+');')
    reply = command(allowCreate=False)
    assert reply['code'] == 'ActivatedExisting', reply
    closed = command(action='close', allowCreate=False)
    assert closed['code'] == 'Closed', closed
    remaining = api('return await api.tabs.query({});')
    assert len([t for t in remaining if t.get('url') == request['url']]) == 1, 'Unrelated tab closed'
    print(json.dumps(dict(browser='Firefox',version=version,minimizedActivations=20,persistentRestart=persistent,
        sessionIdentity=True,unrelatedTabPreserved=True)),flush=True)
finally:
    try:
        m.quit()
    finally:
        m.cleanup()
        m = None
        gc.collect()
