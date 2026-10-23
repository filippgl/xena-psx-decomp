import fs from 'node:fs';
import crypto from 'node:crypto';
const guid = name => { const h = crypto.createHash('md5').update('xena-movement-v1:' + name).digest('hex'); return `${h.slice(0,8)}-${h.slice(8,12)}-${h.slice(12,16)}-${h.slice(16,20)}-${h.slice(20)}`; };
const actions = ['Move', 'LookMouse', 'LookStick', 'Sprint', 'Jump', 'Pause', 'Reset', 'Sword', 'Kick', 'Block', 'CenterCamera', 'Zoom'].map(name => ({ name, id: guid(name),
  type: ['Move','LookMouse','LookStick','Zoom'].includes(name) ? 'Value' : 'Button', expectedControlType: name === 'Zoom' ? 'Axis' : ['Move','LookMouse','LookStick'].includes(name) ? 'Vector2' : 'Button',
  processors: '', interactions: '', initialStateCheck: true }));
const bindings = [];
function bind(action, path, groups, extra = {}) { bindings.push({ name: '', id: guid(action + ':' + path), path, interactions: '', processors: '', groups, action, isComposite: false, isPartOfComposite: false, ...extra }); }
bind('Move','2DVector','KeyboardMouse',{name:'WASD',isComposite:true});
for (const [name,key] of [['Up','w'],['Down','s'],['Left','a'],['Right','d']]) bind('Move',`<Keyboard>/${key}`,'KeyboardMouse',{name,isPartOfComposite:true});
for (const [action,path] of [['LookMouse','<Mouse>/delta'],['Sprint','<Keyboard>/leftShift'],['Jump','<Keyboard>/space'],['Pause','<Keyboard>/escape'],['Reset','<Keyboard>/r']]) bind(action,path,'KeyboardMouse');
for (const [action,path] of [['Move','leftStick'],['LookStick','rightStick'],['Sprint','leftStickPress'],['Jump','buttonSouth'],['Pause','start'],['Reset','select']]) bind(action,'<Gamepad>/'+path,'Gamepad');
for (const [action,path] of [['Sword','<Mouse>/leftButton'],['Kick','<Keyboard>/f'],['Block','<Mouse>/rightButton']]) bind(action,path,'KeyboardMouse');
for (const [action,path] of [['Sword','buttonWest'],['Kick','buttonNorth'],['Block','leftShoulder']]) bind(action,'<Gamepad>/'+path,'Gamepad');
bind('CenterCamera','<Keyboard>/c','KeyboardMouse'); bind('CenterCamera','<Gamepad>/rightStickPress','Gamepad');
bind('Zoom','<Mouse>/scroll/y','KeyboardMouse');
fs.mkdirSync('unity/Packages/com.xena.original-assets/Input',{recursive:true});
fs.writeFileSync('unity/Packages/com.xena.original-assets/Input/XenaPrototype.inputactions',JSON.stringify({name:'XenaPrototype',maps:[{name:'Player',id:guid('Player'),actions,bindings}],controlSchemes:[]},null,2)+'\n');
for (const project of ['XenaUnity','XenaLightingVerification']) {
  const filename=`unity/${project}/ProjectSettings/ProjectSettings.asset`;
  if(fs.existsSync(filename)) fs.writeFileSync(filename,fs.readFileSync(filename,'utf8').replace(/activeInputHandler: \d+/, 'activeInputHandler: 2'));
}
console.log('Prepared movement Input Actions and enabled both input backends.');
