/* X2D startup-screen transport only: no product setter, env writer or focus commands. */
#include "USBBridge.h"
#include <libusb.h>
#include <stdlib.h>
#include <string.h>
#include <stdio.h>
#include <stdarg.h>
#include <time.h>
#include <pthread.h>
#include <zlib.h>
typedef struct {libusb_context *context;libusb_device_handle *device;int claimed;sc_log_callback log;void *log_context;unsigned sequence;uint16_t product_id;} Session;
// Keep one context for the process lifetime. Darwin's detach callback can wait
// on the active-context lock while libusb_exit waits for its hotplug thread.
// Device handles/interfaces are still released on every session close.
static libusb_context *shared_context;
static int context_status;
static pthread_once_t context_once=PTHREAD_ONCE_INIT;
static void initialize_context(void){context_status=libusb_init(&shared_context);}
static void trace(Session *s,const char *format,...){if(!s||!s->log)return;char line[768];va_list args;va_start(args,format);vsnprintf(line,sizeof(line),format,args);va_end(args);s->log(line,s->log_context);}
static int fail(Session *s,char *error,size_t cap,const char *stage,int code){snprintf(error,cap,"USB %s失败：%s（%d）。",stage,libusb_error_name(code),code);trace(s,"%s",error);return code;}
static uint16_t crc(const unsigned char *p,size_t n){uint16_t h=0;while(n--){h^=(uint16_t)*p++<<8;for(int i=0;i<8;i++)h=h&0x8000?(h<<1)^0x1021:h<<1;}return h;}
static void put32(unsigned char *p,uint32_t n){for(int i=0;i<4;i++)p[i]=n>>(8*i);}
static uint32_t get32(const unsigned char *p){return p[0]|(uint32_t)p[1]<<8|(uint32_t)p[2]<<16|(uint32_t)p[3]<<24;}
static uint64_t now(void){struct timespec t;clock_gettime(CLOCK_MONOTONIC,&t);return (uint64_t)t.tv_sec*1000+t.tv_nsec/1000000;}
void *sc_usb_open_logged(sc_log_callback log,void *log_context,char *error,size_t cap){
 Session *s=calloc(1,sizeof(*s));if(!s){snprintf(error,cap,"USB 会话内存分配失败。");return NULL;}s->log=log;s->log_context=log_context;
 trace(s,"USB: libusb 初始化；查找 2756:0009 / 000A");
 const char *stage="初始化";pthread_once(&context_once,initialize_context);int rc=context_status;s->context=shared_context;if(rc)goto failed;
 stage="设备枚举";libusb_device **list=NULL;ssize_t n=libusb_get_device_list(s->context,&list);if(n<0){rc=(int)n;goto failed;}
 libusb_device *found=NULL;int count=0;
 for(ssize_t i=0;i<n;i++){struct libusb_device_descriptor d;if(!libusb_get_device_descriptor(list[i],&d)&&d.idVendor==0x2756&&(d.idProduct==0x0009||d.idProduct==0x000a)){found=list[i];s->product_id=d.idProduct;count++;}}
 if(count!=1){snprintf(error,cap,count?"请只连接一台 X2D 或 CFV 100C 相机。":"未检测到支持的相机，请连接数据线并保持相机开机。");trace(s,"%s",error);libusb_free_device_list(list,1);sc_usb_close(s);return NULL;}
 trace(s,"USB: 检测到一台目标设备");stage="设备打开";rc=libusb_open(found,&s->device);libusb_free_device_list(list,1);if(rc)goto failed;
 stage="接口3占用";rc=libusb_claim_interface(s->device,3);if(rc)goto failed;s->claimed=1;
 stage="接口3配置";rc=libusb_set_interface_alt_setting(s->device,3,0);if(rc)goto failed;
 struct libusb_config_descriptor *config=NULL;stage="端点检查";rc=libusb_get_active_config_descriptor(libusb_get_device(s->device),&config);if(rc)goto failed;
 int endpoints=0;
 for(int i=0;i<config->bNumInterfaces;i++)for(int j=0;j<config->interface[i].num_altsetting;j++){
  const struct libusb_interface_descriptor *d=&config->interface[i].altsetting[j];
  if(d->bInterfaceNumber==3&&d->bAlternateSetting==0)for(int k=0;k<d->bNumEndpoints;k++){
   const struct libusb_endpoint_descriptor *ep=&d->endpoint[k];
   if((ep->bmAttributes&LIBUSB_TRANSFER_TYPE_MASK)==LIBUSB_TRANSFER_TYPE_BULK){if(ep->bEndpointAddress==4)endpoints|=1;if(ep->bEndpointAddress==0x85)endpoints|=2;}
  }
 }
 libusb_free_config_descriptor(config);if(endpoints!=3){rc=LIBUSB_ERROR_NOT_FOUND;goto failed;}
 trace(s,"USB: 接口3 / alt0；OUT 0x04，IN 0x85；回包读取1024字节");return s;
failed:fail(s,error,cap,stage,rc);sc_usb_close(s);return NULL;
}
void *sc_usb_open(char *error,size_t cap){return sc_usb_open_logged(NULL,NULL,error,cap);}
uint16_t sc_usb_product_id(void *p){Session *s=p;return s?s->product_id:0;}
void sc_usb_close(void *p){Session *s=p;if(!s)return;if(s->device){if(s->claimed)libusb_release_interface(s->device,3);libusb_close(s->device);}trace(s,"USB: 会话已关闭");free(s);}
void sc_free(void *p){free(p);}
char *sc_usb_shell(void *ptr,const char *command,unsigned timeout,char *error,size_t cap){
 Session *s=ptr;size_t len=strlen(command);if(!s||len>231||!timeout){snprintf(error,cap,"USB 请求长度或等待时间错误。");return NULL;}
 unsigned char request[257]={10,0,8,5,252},packet[1024];uint32_t cookie=arc4random();if(!cookie)cookie=1;
 put32(request+5,65);put32(request+13,cookie);put32(request+21,10);memcpy(request+25,command,len);put32(request+17,crc(request+21,236));
 unsigned sequence=++s->sequence;int n=0,rc=0;uint64_t started=now(),drain_end=started+50;size_t drained=0;
 while(now()<drain_end){n=0;rc=libusb_bulk_transfer(s->device,0x85,packet,sizeof(packet),&n,5);if(n>0)drained+=(size_t)n;if(rc==LIBUSB_ERROR_TIMEOUT)break;if(rc){fail(s,error,cap,"旧回包清理",rc);return NULL;}}
 trace(s,"USB #%u: 发送 command65，命令%zu字节，等待%ums，已清理%zu字节",sequence,len,timeout,drained);
 n=0;rc=libusb_bulk_transfer(s->device,4,request,257,&n,2000);if(rc||n!=257){if(!rc)rc=LIBUSB_ERROR_IO;fail(s,error,cap,"请求发送",rc);trace(s,"USB #%u: TX=%d/257",sequence,n);return NULL;}
 size_t out_n=0,bn=0,received=0,frames=0,notifications=0,bad_crc=0,foreign=0,discarded=0,partial=0;
 const size_t max=4*1024*1024;char *out=calloc(max+1,1);unsigned char buffer[8192];uint64_t end=now()+timeout;const char *reason="等待最终回复超时";
 if(!out){snprintf(error,cap,"内存不足。");return NULL;}
 const unsigned char header[5]={9,0,5,8,252};
 while(now()<end){
  uint64_t current=now();if(current>=end)break;uint64_t remaining=end-current;unsigned wait=(unsigned)(remaining>200?200:remaining);
  n=0;rc=libusb_bulk_transfer(s->device,0x85,packet,sizeof(packet),&n,wait);
  if(rc!=0&&rc!=LIBUSB_ERROR_TIMEOUT){reason="回包读取";goto failed;}
  // libusb may return valid bytes together with TIMEOUT. Parse them before waiting again.
  if(n<=0)continue;if(rc==LIBUSB_ERROR_TIMEOUT)partial++;
  received+=(size_t)n;if(bn+(size_t)n>sizeof(buffer)){rc=LIBUSB_ERROR_OVERFLOW;reason="回包缓冲区";goto failed;}
  memcpy(buffer+bn,packet,n);bn+=n;
  while(bn>=5){size_t at=0;while(at+5<=bn&&memcmp(buffer+at,header,5))at++;
   if(at+5>bn){if(bn>4){discarded+=bn-4;memmove(buffer,buffer+bn-4,4);bn=4;}break;}
   if(at){discarded+=at;memmove(buffer,buffer+at,bn-at);bn-=at;}if(bn<257)break;
   unsigned char *p=buffer+5;
   // Match the toolkit's byte-by-byte resynchronization; a false header cannot swallow a real frame.
   if(get32(p+12)!=crc(p+16,236)){bad_crc++;discarded++;memmove(buffer,buffer+1,--bn);continue;}
   frames++;
   if(get32(p)==65&&get32(p+8)==cookie){
    uint32_t function=get32(p+4),status=get32(p+16);size_t z=strnlen((char *)p+20,232);
    if(function==4){if(out_n+z>max){rc=LIBUSB_ERROR_OVERFLOW;reason="输出长度限制";goto failed;}memcpy(out+out_n,p+20,z);out_n+=z;notifications++;}
    else {
     if(status){snprintf(error,cap,"USB command65 返回状态 %u（阶段：相机命令执行）。",status);trace(s,"USB #%u: %s；输出%zu字节",sequence,error,out_n);free(out);return NULL;}
     out[out_n]=0;trace(s,"USB #%u: 完成；RX=%zu，帧=%zu，通知=%zu，输出=%zu字节，CRC异常=%zu，其他请求=%zu，填充=%zu，超时带数据=%zu，用时=%llums",sequence,received,frames,notifications,out_n,bad_crc,foreign,discarded,partial,(unsigned long long)(now()-started));return out;
    }
   }else foreign++;
   memmove(buffer,buffer+257,bn-257);bn-=257;
  }
 }
 rc=LIBUSB_ERROR_TIMEOUT;
failed:free(out);fail(s,error,cap,reason,rc);
 trace(s,"USB #%u: RX=%zu，帧=%zu，通知=%zu，输出=%zu字节，CRC异常=%zu，其他请求=%zu，残留=%zu，超时带数据=%zu，用时=%llums",sequence,received,frames,notifications,out_n,bad_crc,foreign,bn,partial,(unsigned long long)(now()-started));return NULL;
}
uint8_t *sc_gzip(const uint8_t *data,size_t size,size_t *outsize){
 z_stream s={0};if(deflateInit2(&s,6,Z_DEFLATED,31,8,Z_DEFAULT_STRATEGY)!=Z_OK)return NULL;
 size_t capacity=deflateBound(&s,size);uint8_t *b=malloc(capacity);if(!b){deflateEnd(&s);return NULL;}
 s.next_in=(Bytef *)data;s.avail_in=(uInt)size;s.next_out=b;s.avail_out=(uInt)capacity;
 int rc=deflate(&s,Z_FINISH);*outsize=s.total_out;deflateEnd(&s);if(rc!=Z_STREAM_END){free(b);return NULL;}return b;
}
