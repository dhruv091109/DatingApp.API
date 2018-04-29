using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using DatingApp.API.Data;
using DatingApp.API.Dtos;
using DatingApp.API.Helpers;
using DatingApp.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DatingApp.API.Controllers
{
    [Authorize]
    [Route("api/users/{userId}/photos")]
    public class PhotosController : Controller
    {
        private IMapper _mapper;
        private IDatingRepository _repo;
        private IOptions<CloudinarySettings> _cloudinaryConfig;
        private Cloudinary _cloudinary;

        public PhotosController(IDatingRepository repo, 
        IMapper mapper, 
        IOptions<CloudinarySettings> cloudinaryConfig)
        {
            this._mapper = mapper;
            this._repo = repo;
            this._cloudinaryConfig = cloudinaryConfig;

            Account acc = new Account(
                _cloudinaryConfig.Value.CloudName,
                _cloudinaryConfig.Value.ApiKey,
                _cloudinaryConfig.Value.ApiSecret
            );
            _cloudinary = new Cloudinary(acc);
        }       

        [HttpGet("{id}", Name = "GetPhoto")]
        public async Task<IActionResult> GetPhoto(int id)
        {
            var photoFromRepo = _repo.GetPhoto(id);
            var photo = _mapper.Map<PhotoForReturnDto>(photoFromRepo);
            return Ok(photo);
        }

        [HttpPost]
        public async Task<IActionResult> AddPhotoForUser(int userId,PhotoForCreationDto photoDto )
        {
            var user = await _repo.GetUser(userId);
            if(user == null)
            {
                return BadRequest("Could not find user");
            }
            var curruntUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
            if(curruntUserId != userId)
            {
                return Unauthorized();
            }
            var file = photoDto.File;
            var uploadResult = new ImageUploadResult();
            if(file.Length > 0)
            {
                using (var stream = file.OpenReadStream())
                {
                    var uploadParams = new ImageUploadParams()
                    {
                        File = new FileDescription(file.Name , stream)
                    };
                    uploadResult = _cloudinary.Upload(uploadParams);
                }
            }

            photoDto.Url = uploadResult.Uri.ToString();
            photoDto.PublicId = uploadResult.PublicId;

            var photo = _mapper.Map<Photo>(photoDto);
            photo.User = user;

            if(!user.Photos.Any(m => m.IsMain))
                photo.IsMain = true;
            
            user.Photos.Add(photo);
            var photoToReturn = _mapper.Map<PhotoForReturnDto>(photo);
            if(await _repo.SaveAll())
            {
                return CreatedAtRoute("GetPhoto",new {id = photo.Id},photoToReturn);
            }
            return BadRequest("cant upload the photo");
        } 

        [HttpPost("{id}/setMain")]
        public async Task<IActionResult> SetMainPhoto(int userId, int id)
        {
            if(userId != int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value))
                return Unauthorized();

            var photoFromRepo = await _repo.GetPhoto(id);
            if(photoFromRepo.IsMain)
                return BadRequest("Already set to main photo");
            var curruntMainPhoto = await _repo.GetMainPhotoFormUser(userId);
            if(curruntMainPhoto != null){
                curruntMainPhoto.IsMain = false;
            }
            
            photoFromRepo.IsMain = true;
            if(await _repo.SaveAll()){
                return NoContent();
            }
            return BadRequest("cant set photo as a main photo");
        } 

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePhoto(int userId , int id)
        {
            if(userId != int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value))
                return Unauthorized();

            var photoFromRepo = await _repo.GetPhoto(id);
            if(photoFromRepo == null)
                return NotFound();

            if(photoFromRepo.IsMain)
                return BadRequest("cant delete main photo");
            
           if (photoFromRepo.PublicId != null)
           {
                var deleteparams = new DeletionParams(photoFromRepo.PublicId);
                var result = _cloudinary.Destroy(deleteparams);
                
                if (result.Result == "ok") 
                    _repo.Delete(photoFromRepo);
           }
            if (photoFromRepo.PublicId == null)
            {
                _repo.Delete(photoFromRepo);
            }
            
            if(await _repo.SaveAll())
                return Ok();
            return BadRequest("Failed to delete photo");

        }
    }
}